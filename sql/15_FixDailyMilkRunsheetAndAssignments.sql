-- ====================================================================================
-- Script: 15_FixDailyMilkRunsheetAndAssignments.sql
-- Description:
--   1. Aligns dbo.usp_DailyMilk_GetRoster with dbo.usp_DailyMilk_SaveBatch by verifying
--      active CustomerDeliveryBoyAssignments covering @EntryDate (or existing delivery rows),
--      eliminating unexpected Error 50021 batch rejections on Draft/Submit.
--   2. Updates dbo.usp_DailyMilk_SaveBatch so Approved Vacation customers locked as
--      'Not Delivered' do not throw Error 50022 on batch SaveDraft/Submit, while still
--      strictly blocking 'Completed' or 'Pending' deliveries on approved vacation dates.
--
-- Target DB: DairyManagementDb (Idempotent deployment)
-- ====================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
BEGIN
    DECLARE @SysDbMsg NVARCHAR(512) = N'Deployment halted: Cannot run against system database [' + DB_NAME() + N'].';
    THROW 50000, @SysDbMsg, 1;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'db_dms_app') IS NULL
BEGIN
    DECLARE @RoleMsg NVARCHAR(512) = N'Deployment halted: Role [db_dms_app] does not exist in [' + DB_NAME() + N'].';
    THROW 50001, @RoleMsg, 1;
END;
GO

PRINT '====================================================================================';
PRINT 'Deploying 15_FixDailyMilkRunsheetAndAssignments.sql on [' + DB_NAME() + ']...';
PRINT '====================================================================================';
GO

-- ------------------------------------------------------------------------------------
-- 1. Align dbo.usp_DailyMilk_GetRoster With Assignment Coverage
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_GetRoster]
    @DeliveryBoyId INT = NULL,
    @RouteId INT = NULL,
    @EntryDate DATE,
    @Shift NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.CustomerId,
        c.CustomerCode,
        c.FullName,
        c.FullNameGujarati,
        c.Phone,
        c.Address,
        c.Area,
        c.Landmark,
        c.DeliverySequence,
        c.RouteId,
        r.RouteName,
        c.CurrentDeliveryBoyId AS DeliveryBoyId,
        u.FullName AS DeliveryBoyName,
        dmd.DeliveryId,
        ISNULL(dmd.Status, 'Pending') AS DeliveryStatus,
        dmd.NonDeliveryReason,
        ISNULL(dmd.TotalLiquidVolumeLitres, 0.000) AS TotalLiquidVolumeLitres,
        ISNULL(dmd.TotalAmount, 0.00) AS TotalAmount,
        ISNULL(dmd.IsUnscheduledDelivery, 0) AS IsUnscheduledDelivery,
        ISNULL(dmd.IsCorrected, 0) AS IsCorrected,
        CASE WHEN vac.VacationId IS NOT NULL THEN 1 ELSE 0 END AS IsOnVacation,
        vac.Reason AS VacationReason
    FROM dbo.Customers c
    JOIN dbo.Routes r ON c.RouteId = r.RouteId
    LEFT JOIN dbo.DeliveryBoys db ON c.CurrentDeliveryBoyId = db.DeliveryBoyId
    LEFT JOIN dbo.Users u ON db.UserId = u.UserId
    LEFT JOIN dbo.DailyMilkDeliveries dmd ON dmd.CustomerId = c.CustomerId
        AND dmd.EntryDate = @EntryDate
        AND dmd.Shift = @Shift
    LEFT JOIN dbo.CustomerVacations vac ON vac.CustomerId = c.CustomerId
        AND vac.Status = 'Approved'
        AND @EntryDate BETWEEN vac.StartDate AND vac.EndDate
        AND (vac.Shift = 'Both' OR vac.Shift = @Shift)
    WHERE c.IsActive = 1
      AND (
          dmd.DeliveryId IS NOT NULL
          OR (
              (@DeliveryBoyId IS NULL OR c.CurrentDeliveryBoyId = @DeliveryBoyId)
              AND (@RouteId IS NULL OR c.RouteId = @RouteId)
              AND (
                  @DeliveryBoyId IS NULL
                  OR EXISTS (
                      SELECT 1
                      FROM dbo.CustomerDeliveryBoyAssignments a
                      WHERE a.CustomerId = c.CustomerId
                        AND a.DeliveryBoyId = @DeliveryBoyId
                        AND a.AssignedFrom <= @EntryDate
                        AND (a.AssignedTo IS NULL OR a.AssignedTo >= @EntryDate)
                        AND a.IsActive = 1
                  )
              )
          )
      )
      AND (c.PreferredShift = 'Both' OR c.PreferredShift = @Shift)
    ORDER BY c.DeliverySequence ASC, c.CustomerCode ASC;
END;
GO

-- ------------------------------------------------------------------------------------
-- 2. Update dbo.usp_DailyMilk_SaveBatch (Vacation 'Not Delivered' Lock Handling)
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_SaveBatch]
    @DeliveryBoyId INT,
    @EntryDate DATE,
    @Shift NVARCHAR(20),
    @Status NVARCHAR(20), -- 'Draft' or 'Submitted'
    @Headers [dbo].[DailyMilkDeliveryHeaderInputType] READONLY,
    @Items [dbo].[DailyMilkDeliveryItemInputType] READONLY,
    @CreatedByUserId INT,
    @BatchId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@Shift NOT IN ('Morning', 'Evening'))
    BEGIN
        THROW 50033, 'Shift must be ''Morning'' or ''Evening''.', 1;
    END

    IF (@Status NOT IN ('Draft', 'Submitted'))
    BEGIN
        THROW 50034, 'Status must be ''Draft'' or ''Submitted''.', 1;
    END

    -- Retrieve Delivery Boy Details
    DECLARE @CompensationType NVARCHAR(20), @PrimaryRouteId INT, @IsDeliveryBoyActive BIT;
    SELECT
        @CompensationType = CompensationType,
        @PrimaryRouteId = PrimaryRouteId,
        @IsDeliveryBoyActive = IsActive
    FROM dbo.DeliveryBoys
    WHERE DeliveryBoyId = @DeliveryBoyId;

    IF (@CompensationType IS NULL OR @IsDeliveryBoyActive = 0)
    BEGIN
        THROW 50037, 'Delivery boy does not exist or is inactive.', 1;
    END

    -- Resolve Effective Commission Rate
    DECLARE @EffectiveCommissionRate DECIMAL(18,2) = NULL;
    IF (@CompensationType = 'Fixed Salary')
    BEGIN
        SET @EffectiveCommissionRate = 0.00;
    END
    ELSE IF (@CompensationType = 'Commission')
    BEGIN
        SELECT TOP 1 @EffectiveCommissionRate = CommissionRatePerLitre
        FROM dbo.DeliveryBoyCommissionRates
        WHERE DeliveryBoyId = @DeliveryBoyId
          AND EffectiveFrom <= @EntryDate
          AND (EffectiveTo IS NULL OR EffectiveTo >= @EntryDate)
        ORDER BY EffectiveFrom DESC;

        -- ZERO SILENT FALLBACK INVARIANT
        IF (@EffectiveCommissionRate IS NULL AND @Status = 'Submitted')
        BEGIN
            THROW 50035, 'Cannot finalize shift: No verified commission rate configured for this delivery boy covering EntryDate. An administrator must explicitly configure an effective-dated commission rate tier covering this date before finalizing this shift.', 1;
        END
        ELSE IF (@EffectiveCommissionRate IS NULL)
        BEGIN
            SET @EffectiveCommissionRate = 0.00; -- Allowed for preliminary draft saving only
        END
    END

    BEGIN TRANSACTION;

    -- Check if batch already submitted
    DECLARE @ExistingBatchId BIGINT, @ExistingBatchStatus NVARCHAR(20);
    SELECT @ExistingBatchId = BatchId, @ExistingBatchStatus = Status
    FROM dbo.DailyDeliveryBatches WITH (UPDLOCK, HOLDLOCK)
    WHERE DeliveryBoyId = @DeliveryBoyId AND EntryDate = @EntryDate AND Shift = @Shift;

    IF (@ExistingBatchStatus = 'Submitted')
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50020, 'Shift batch has already been finalized and submitted. No further modifications permitted.', 1;
    END

    -- 1. Validate Customer Assignments
    DECLARE @UnassignedCustomerId INT;
    SELECT TOP 1 @UnassignedCustomerId = h.CustomerId
    FROM @Headers h
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.CustomerDeliveryBoyAssignments a
        WHERE a.CustomerId = h.CustomerId
          AND a.DeliveryBoyId = @DeliveryBoyId
          AND a.AssignedFrom <= @EntryDate
          AND (a.AssignedTo IS NULL OR a.AssignedTo >= @EntryDate)
          AND a.IsActive = 1
    );

    IF (@UnassignedCustomerId IS NOT NULL)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50021, 'Customer is not assigned to delivery boy on this date.', 1;
    END

    -- 2. Validate Approved Vacation Conflict (only block non-'Not Delivered' statuses)
    DECLARE @VacationConflictCustomerId INT;
    SELECT TOP 1 @VacationConflictCustomerId = h.CustomerId
    FROM @Headers h
    WHERE h.Status <> 'Not Delivered'
      AND EXISTS (
          SELECT 1
          FROM dbo.CustomerVacations v
          WHERE v.CustomerId = h.CustomerId
            AND v.Status = 'Approved'
            AND @EntryDate BETWEEN v.StartDate AND v.EndDate
            AND (v.Shift = 'Both' OR v.Shift = @Shift)
      );

    IF (@VacationConflictCustomerId IS NOT NULL)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50022, 'Customer is on approved vacation for this shift and date.', 1;
    END

    -- 3. Validate Unscheduled Delivery Authorizations
    DECLARE @UnauthorizedCustomerId INT;
    SELECT TOP 1 @UnauthorizedCustomerId = h.CustomerId
    FROM @Headers h
    WHERE h.IsUnscheduledDelivery = 1
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.CustomerUnscheduledDeliveryAuthorizations auth
          WHERE auth.CustomerId = h.CustomerId
            AND auth.EntryDate = @EntryDate
            AND auth.IsRevoked = 0
            AND (auth.AuthorizedShift = 'Both' OR auth.AuthorizedShift = @Shift)
            AND NOT EXISTS (
                SELECT 1
                FROM dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions c
                WHERE c.AuthorizationId = auth.AuthorizationId
                  AND c.ConsumedShift = @Shift
            )
      );

    IF (@UnauthorizedCustomerId IS NOT NULL)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50025, 'Unscheduled delivery on off-schedule date is not authorized by an Administrator for this shift.', 1;
    END

    -- Upsert Batch Header
    IF (@ExistingBatchId IS NULL)
    BEGIN
        INSERT INTO dbo.DailyDeliveryBatches (
            DeliveryBoyId, RouteId, EntryDate, Shift, Status, TotalAssignedCustomers,
            CompletedCount, PendingCount, NotDeliveredCount, TotalLiquidVolumeLitres, TotalAmount,
            StartedAt, SubmittedAt, SubmittedBy, CreatedAt, CreatedBy
        )
        VALUES (
            @DeliveryBoyId, @PrimaryRouteId, @EntryDate, @Shift, @Status, 0,
            0, 0, 0, 0.000, 0.00,
            SYSUTCDATETIME(),
            CASE WHEN @Status = 'Submitted' THEN SYSUTCDATETIME() ELSE NULL END,
            CASE WHEN @Status = 'Submitted' THEN @CreatedByUserId ELSE NULL END,
            SYSUTCDATETIME(), @CreatedByUserId
        );
        SET @BatchId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        SET @BatchId = @ExistingBatchId;
        UPDATE dbo.DailyDeliveryBatches
        SET Status = @Status,
            SubmittedAt = CASE WHEN @Status = 'Submitted' AND SubmittedAt IS NULL THEN SYSUTCDATETIME() ELSE SubmittedAt END,
            SubmittedBy = CASE WHEN @Status = 'Submitted' AND SubmittedBy IS NULL THEN @CreatedByUserId ELSE SubmittedBy END,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @CreatedByUserId
        WHERE BatchId = @BatchId;
    END

    -- Temporary table to hold deliveries for this batch
    DECLARE @DeliveryMapping TABLE (
        [CustomerId] INT NOT NULL,
        [DeliveryId] BIGINT NOT NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [IsUnscheduled] BIT NOT NULL
    );

    -- Cursor over headers to upsert DailyMilkDeliveries
    DECLARE @CustId INT, @CustStatus NVARCHAR(20), @NonDelivReason NVARCHAR(250), @IsUnscheduled BIT;
    DECLARE curHeaders CURSOR LOCAL FAST_FORWARD FOR
        SELECT CustomerId, Status, NonDeliveryReason, IsUnscheduledDelivery
        FROM @Headers;

    OPEN curHeaders;
    FETCH NEXT FROM curHeaders INTO @CustId, @CustStatus, @NonDelivReason, @IsUnscheduled;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @DeliveryId BIGINT = NULL;
        DECLARE @AuthId BIGINT = NULL, @AdminAuthBy INT = NULL;
        DECLARE @VacId BIGINT = NULL;

        IF (@CustStatus = 'Not Delivered')
        BEGIN
            SELECT TOP 1 @VacId = v.VacationId
            FROM dbo.CustomerVacations v
            WHERE v.CustomerId = @CustId
              AND v.Status = 'Approved'
              AND @EntryDate BETWEEN v.StartDate AND v.EndDate
              AND (v.Shift = 'Both' OR v.Shift = @Shift)
            ORDER BY v.VacationId DESC;

            IF (@VacId IS NOT NULL AND ( @NonDelivReason IS NULL OR LTRIM(RTRIM(@NonDelivReason)) = N'' ))
            BEGIN
                SET @NonDelivReason = N'Approved Vacation';
            END
        END

        -- Resolve authorization ID if unscheduled
        IF (@IsUnscheduled = 1)
        BEGIN
            SELECT TOP 1 @AuthId = auth.AuthorizationId, @AdminAuthBy = auth.AuthorizedBy
            FROM dbo.CustomerUnscheduledDeliveryAuthorizations auth
            WHERE auth.CustomerId = @CustId
              AND auth.EntryDate = @EntryDate
              AND auth.IsRevoked = 0
              AND (auth.AuthorizedShift = 'Both' OR auth.AuthorizedShift = @Shift)
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions c
                  WHERE c.AuthorizationId = auth.AuthorizationId
                    AND c.ConsumedShift = @Shift
              )
            ORDER BY auth.AuthorizationId ASC;
        END

        SELECT @DeliveryId = DeliveryId
        FROM dbo.DailyMilkDeliveries
        WHERE CustomerId = @CustId AND EntryDate = @EntryDate AND Shift = @Shift;

        IF (@DeliveryId IS NULL)
        BEGIN
            INSERT INTO dbo.DailyMilkDeliveries (
                CustomerId, DeliveryBoyId, EntryDate, Shift, Status, VacationId, NonDeliveryReason,
                CommissionRateSnapshot, TotalLiquidVolumeLitres, EligibleLitreQuantity, TotalAmount,
                BatchId, IsUnscheduledDelivery, AuthorizationId, AdminAuthorizedBy, CreatedAt, CreatedBy
            )
            VALUES (
                @CustId, @DeliveryBoyId, @EntryDate, @Shift, @CustStatus, @VacId, @NonDelivReason,
                @EffectiveCommissionRate, 0.000, 0.000, 0.00,
                @BatchId, @IsUnscheduled, @AuthId, @AdminAuthBy, SYSUTCDATETIME(), @CreatedByUserId
            );
            SET @DeliveryId = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.DailyMilkDeliveries
            SET DeliveryBoyId = @DeliveryBoyId,
                Status = @CustStatus,
                VacationId = ISNULL(@VacId, VacationId),
                NonDeliveryReason = @NonDelivReason,
                CommissionRateSnapshot = @EffectiveCommissionRate,
                BatchId = @BatchId,
                IsUnscheduledDelivery = @IsUnscheduled,
                AuthorizationId = ISNULL(@AuthId, AuthorizationId),
                AdminAuthorizedBy = ISNULL(@AdminAuthBy, AdminAuthorizedBy),
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @CreatedByUserId
            WHERE DeliveryId = @DeliveryId;
        END

        INSERT INTO @DeliveryMapping (CustomerId, DeliveryId, Status, IsUnscheduled)
        VALUES (@CustId, @DeliveryId, @CustStatus, @IsUnscheduled);

        -- Delete old items for this delivery and insert new ones
        DELETE FROM dbo.DailyMilkDeliveryItems WHERE DeliveryId = @DeliveryId;

        -- Only insert category items if status is Completed
        IF (@CustStatus = 'Completed')
        BEGIN
            INSERT INTO dbo.DailyMilkDeliveryItems (
                DeliveryId, CategoryId, PackagingQuantity, ProductTypeSnapshot, UnitOfMeasureSnapshot,
                PricingBasisSnapshot, BillingUnitEquivalentSnapshot, LitreEquivalentSnapshot,
                LiquidVolumeLitres, UnitPriceSnapshot, SubTotal
            )
            SELECT
                @DeliveryId,
                item.CategoryId,
                item.PackagingQuantity,
                cat.ProductType,
                cat.UnitOfMeasure,
                cat.PricingBasis,
                cat.BillingUnitEquivalent,
                cat.LitreEquivalent,
                ROUND(item.PackagingQuantity * cat.LitreEquivalent, 3),
                ISNULL(cp.CustomPrice, cat.DefaultPrice),
                ROUND(item.PackagingQuantity * ISNULL(cp.CustomPrice, cat.DefaultPrice), 2)
            FROM @Items item
            JOIN dbo.MilkCategories cat ON item.CategoryId = cat.CategoryId
            LEFT JOIN dbo.CustomerCustomPricing cp ON cp.CustomerId = @CustId
                AND cp.CategoryId = item.CategoryId
                AND cp.IsActive = 1
                AND cp.EffectiveFrom <= @EntryDate
                AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @EntryDate)
            WHERE item.CustomerId = @CustId;
        END

        -- Recompute delivery line rollups
        DECLARE @DelivVolume DECIMAL(18,3) = 0.000, @DelivAmount DECIMAL(18,2) = 0.00;
        SELECT
            @DelivVolume = ISNULL(SUM(LiquidVolumeLitres), 0.000),
            @DelivAmount = ISNULL(SUM(SubTotal), 0.00)
        FROM dbo.DailyMilkDeliveryItems
        WHERE DeliveryId = @DeliveryId;

        UPDATE dbo.DailyMilkDeliveries
        SET TotalLiquidVolumeLitres = @DelivVolume,
            EligibleLitreQuantity = CASE WHEN @CustStatus = 'Completed' THEN @DelivVolume ELSE 0.000 END,
            TotalAmount = CASE WHEN @CustStatus = 'Completed' THEN @DelivAmount ELSE 0.00 END
        WHERE DeliveryId = @DeliveryId;

        -- Record unscheduled consumption if final submission
        IF (@IsUnscheduled = 1 AND @AuthId IS NOT NULL AND @Status = 'Submitted')
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions
                WHERE AuthorizationId = @AuthId AND ConsumedShift = @Shift
            )
            BEGIN
                INSERT INTO dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions (
                    AuthorizationId, DeliveryId, ConsumedShift, ConsumedAt
                )
                VALUES (@AuthId, @DeliveryId, @Shift, SYSUTCDATETIME());
            END
        END

        FETCH NEXT FROM curHeaders INTO @CustId, @CustStatus, @NonDelivReason, @IsUnscheduled;
    END

    CLOSE curHeaders;
    DEALLOCATE curHeaders;

    -- Update Batch Summary Rollups
    DECLARE @TotalAssigned INT = 0, @CompCount INT = 0, @PendCount INT = 0, @NotDelivCount INT = 0;
    DECLARE @BatchVolume DECIMAL(18,3) = 0.000, @BatchAmount DECIMAL(18,2) = 0.00;

    SELECT
        @TotalAssigned = COUNT(*),
        @CompCount = SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END),
        @PendCount = SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END),
        @NotDelivCount = SUM(CASE WHEN Status = 'Not Delivered' THEN 1 ELSE 0 END),
        @BatchVolume = ISNULL(SUM(TotalLiquidVolumeLitres), 0.000),
        @BatchAmount = ISNULL(SUM(TotalAmount), 0.00)
    FROM dbo.DailyMilkDeliveries
    WHERE BatchId = @BatchId;

    UPDATE dbo.DailyDeliveryBatches
    SET TotalAssignedCustomers = @TotalAssigned,
        CompletedCount = @CompCount,
        PendingCount = @PendCount,
        NotDeliveredCount = @NotDelivCount,
        TotalLiquidVolumeLitres = @BatchVolume,
        TotalAmount = @BatchAmount
    WHERE BatchId = @BatchId;

    COMMIT TRANSACTION;
END;
GO

GRANT EXECUTE ON [dbo].[usp_DailyMilk_GetRoster] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DailyMilk_SaveBatch] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT 'Completed 15_FixDailyMilkRunsheetAndAssignments.sql.';
PRINT '====================================================================================';
GO
