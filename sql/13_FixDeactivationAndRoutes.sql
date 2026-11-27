-- ====================================================================================
-- Script: 13_FixDeactivationAndRoutes.sql
-- Description:
--   1. Fixes CHECK constraint conflicts in dbo.usp_DeliveryBoy_Deactivate and
--      dbo.usp_Customer_Deactivate_WithRecordedDelivery when existing effective/assignment
--      start dates are after today's / deactivation date (clamps end dates >= start dates).
--   2. Adds full Route Management stored procedures (GetAll, Create, Update, SetActive)
--      so Administrators can dynamically add, edit, and manage routes without hardcoded
--      or fixed route lists.
--   3. Grants least-privilege EXECUTE permissions on new procedures to [db_dms_app].
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
PRINT 'Deploying 13_FixDeactivationAndRoutes.sql on [' + DB_NAME() + ']...';
PRINT '====================================================================================';
GO

-- ------------------------------------------------------------------------------------
-- 1. Fix dbo.usp_Customer_Deactivate_WithRecordedDelivery (Safe End-Date Clamping)
-- Ensures AssignedTo >= AssignedFrom and EffectiveTo >= EffectiveFrom even when
-- future-dated customer assignments or subscriptions are closed early.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Customer_Deactivate_WithRecordedDelivery]
    @CustomerId INT,
    @DeactivationDate DATE, -- The date on which delivery was already recorded
    @Reason NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. Lock Customer Row
    SELECT 1 FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK) WHERE [CustomerId] = @CustomerId;

    -- 2. Master Record Soft Delete
    UPDATE [dbo].[Customers]
    SET [Status] = 'Inactive',
        [IsActive] = 0,
        [CurrentDeliveryBoyId] = NULL,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId;

    -- 3. Close Active Assignment ending ON DeactivationDate D (Clamped >= AssignedFrom)
    UPDATE [dbo].[CustomerDeliveryBoyAssignments]
    SET [AssignedTo] = CASE
            WHEN [AssignedFrom] <= @DeactivationDate THEN @DeactivationDate
            ELSE [AssignedFrom]
        END,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId,
        [Notes] = ISNULL([Notes] + ' | ', '') + 'Closed on confirmed deactivation; recorded deliveries preserved'
    WHERE [CustomerId] = @CustomerId AND [AssignedTo] IS NULL;

    -- 4. Close Active Subscriptions ending ON DeactivationDate D (Clamped >= EffectiveFrom)
    UPDATE [dbo].[CustomerSubscriptions]
    SET [EffectiveTo] = CASE
            WHEN [EffectiveFrom] <= @DeactivationDate THEN @DeactivationDate
            ELSE [EffectiveFrom]
        END,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 5. Close Custom Pricing ending ON DeactivationDate D (Clamped >= EffectiveFrom)
    UPDATE [dbo].[CustomerCustomPricing]
    SET [EffectiveTo] = CASE
            WHEN [EffectiveFrom] <= @DeactivationDate THEN @DeactivationDate
            ELSE [EffectiveFrom]
        END,
        [IsActive] = 0
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 6. Cancel Future Scheduled Vacations starting after Date D
    UPDATE [dbo].[CustomerVacations]
    SET [Status] = 'Cancelled',
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND [Status] IN ('Pending', 'Approved') AND [StartDate] > @DeactivationDate;

    -- 7. Audit Log with Explicit Preserved Delivery Flag
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
    BEGIN
        INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
        VALUES (@AdminUserId, 'DEACTIVATE_CONFIRMED_WITH_DELIVERY', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
                CONCAT('{"DeactivationDate":"', CONVERT(VARCHAR(10), @DeactivationDate, 120), '","BilledDeliveryPreserved":1,"Reason":"', REPLACE(ISNULL(@Reason, ''), '"', '\"'), '"}'),
                SYSUTCDATETIME());
    END

    COMMIT TRANSACTION;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Customer_Deactivate_WithRecordedDelivery] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 2. Fix dbo.usp_DeliveryBoy_Deactivate (Safe End-Date Clamping)
-- Clamps EffectiveTo >= EffectiveFrom on DeliveryBoyCommissionRates and
-- AssignedTo >= AssignedFrom on CustomerDeliveryBoyAssignments in all branches.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_Deactivate]
    @DeliveryBoyId INT,
    @ReassignToDeliveryBoyId INT = NULL,
    @Reason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Lock DeliveryBoys row
        DECLARE @UserId INT;
        DECLARE @IsActive BIT;

        SELECT @UserId = UserId, @IsActive = IsActive
        FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
        WHERE DeliveryBoyId = @DeliveryBoyId;

        IF (@UserId IS NULL)
        BEGIN
            THROW 50045, 'Delivery boy record does not exist.', 1;
        END

        -- 2. Lock Users row
        SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId;

        -- 3. Verify active status
        IF (@IsActive = 0)
        BEGIN
            THROW 50053, 'Delivery boy is already deactivated.', 1;
        END

        -- 4. Disable Users account (blocks login)
        UPDATE dbo.Users
        SET IsActive = 0,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE UserId = @UserId;

        -- 5. Disable DeliveryBoys profile
        UPDATE dbo.DeliveryBoys
        SET IsActive = 0,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE DeliveryBoyId = @DeliveryBoyId;

        -- 6. Close open-ended commission rate tier safely (clamp >= EffectiveFrom)
        UPDATE dbo.DeliveryBoyCommissionRates
        SET EffectiveTo = CASE
                WHEN EffectiveFrom <= @Today THEN @Today
                ELSE EffectiveFrom
            END
        WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveTo IS NULL;

        -- 7. Handle customer assignments
        IF (@ReassignToDeliveryBoyId IS NOT NULL)
        BEGIN
            IF (@ReassignToDeliveryBoyId = @DeliveryBoyId)
            BEGIN
                THROW 50008, 'Cannot reassign customers to the same delivery boy being deactivated.', 1;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryBoys WHERE DeliveryBoyId = @ReassignToDeliveryBoyId AND IsActive = 1)
            BEGIN
                THROW 50008, 'Target delivery boy does not exist or is inactive.', 1;
            END

            -- Reassign active customers
            DECLARE @ReassignCustId INT;
            DECLARE curCusts CURSOR LOCAL FAST_FORWARD FOR
                SELECT CustomerId
                FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK)
                WHERE CurrentDeliveryBoyId = @DeliveryBoyId AND IsActive = 1;

            OPEN curCusts;
            FETCH NEXT FROM curCusts INTO @ReassignCustId;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                -- Close active assignment (clamp >= AssignedFrom)
                UPDATE dbo.CustomerDeliveryBoyAssignments
                SET AssignedTo = CASE
                        WHEN AssignedFrom <= @Today THEN @Today
                        ELSE AssignedFrom
                    END,
                    IsActive = 0,
                    UpdatedAt = SYSUTCDATETIME(),
                    UpdatedBy = @AdminUserId,
                    Notes = CASE
                        WHEN AssignedFrom > @Today THEN ISNULL(Notes + ' | ', '') + 'Closed early upon delivery boy deactivation'
                        ELSE Notes
                    END
                WHERE CustomerId = @ReassignCustId AND DeliveryBoyId = @DeliveryBoyId AND IsActive = 1 AND AssignedTo IS NULL;

                -- Insert replacement assignment
                INSERT INTO dbo.CustomerDeliveryBoyAssignments (
                    CustomerId, DeliveryBoyId, AssignedFrom, AssignedTo, IsActive, Notes, CreatedAt, CreatedBy
                )
                VALUES (
                    @ReassignCustId, @ReassignToDeliveryBoyId, @Today, NULL, 1, @Reason, SYSUTCDATETIME(), @AdminUserId
                );

                -- Update customer pointer
                UPDATE dbo.Customers
                SET CurrentDeliveryBoyId = @ReassignToDeliveryBoyId,
                    UpdatedAt = SYSUTCDATETIME(),
                    UpdatedBy = @AdminUserId
                WHERE CustomerId = @ReassignCustId;

                FETCH NEXT FROM curCusts INTO @ReassignCustId;
            END

            CLOSE curCusts;
            DEALLOCATE curCusts;

            -- Clean up any remaining active assignments pointing to the deactivated delivery boy
            UPDATE dbo.CustomerDeliveryBoyAssignments
            SET AssignedTo = CASE
                    WHEN AssignedFrom <= @Today THEN @Today
                    ELSE AssignedFrom
                END,
                IsActive = 0,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE DeliveryBoyId = @DeliveryBoyId AND IsActive = 1 AND AssignedTo IS NULL;

            UPDATE dbo.Customers
            SET CurrentDeliveryBoyId = NULL,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE CurrentDeliveryBoyId = @DeliveryBoyId;
        END
        ELSE
        BEGIN
            -- Close assignments (clamp >= AssignedFrom) and set CurrentDeliveryBoyId to NULL
            UPDATE dbo.CustomerDeliveryBoyAssignments
            SET AssignedTo = CASE
                    WHEN AssignedFrom <= @Today THEN @Today
                    ELSE AssignedFrom
                END,
                IsActive = 0,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId,
                Notes = CASE
                    WHEN AssignedFrom > @Today THEN ISNULL(Notes + ' | ', '') + 'Closed early upon delivery boy deactivation'
                    ELSE Notes
                END
            WHERE DeliveryBoyId = @DeliveryBoyId AND IsActive = 1 AND AssignedTo IS NULL;

            UPDATE dbo.Customers
            SET CurrentDeliveryBoyId = NULL,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE CurrentDeliveryBoyId = @DeliveryBoyId;
        END

        -- 8. Historical DailyMilkDeliveries remain untouched
        -- 9. Existing DeliveryBoyMonthlyCompensations remain intact

        -- 10. Audit log entry
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO dbo.AuditLogs (UserId, Action, TableName, RecordId, NewValuesJson, Timestamp)
            VALUES (@AdminUserId, 'DEACTIVATE', 'DeliveryBoys', CAST(@DeliveryBoyId AS VARCHAR(50)), 
                    '{"DeliveryBoyId":' + CAST(@DeliveryBoyId AS NVARCHAR(20)) + ',"Reason":"' + REPLACE(ISNULL(@Reason, ''), '"', '\"') + '"}',
                    SYSUTCDATETIME());
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local', 'curCusts') >= 0
        BEGIN
            CLOSE curCusts;
            DEALLOCATE curCusts;
        END

        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_Deactivate] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 3. Route Management Stored Procedures
-- ------------------------------------------------------------------------------------

-- 3.1 Get All Routes with Active Delivery Boy & Customer Counts
CREATE OR ALTER PROCEDURE [dbo].[usp_Routes_GetAll]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        r.[RouteId],
        r.[RouteCode],
        r.[RouteName],
        r.[Description],
        r.[IsActive],
        r.[CreatedAt],
        r.[UpdatedAt],
        ISNULL(db_counts.[ActiveDeliveryBoyCount], 0) AS [ActiveDeliveryBoyCount],
        ISNULL(cust_counts.[ActiveCustomerCount], 0) AS [ActiveCustomerCount]
    FROM [dbo].[Routes] r
    OUTER APPLY (
        SELECT COUNT(1) AS [ActiveDeliveryBoyCount]
        FROM [dbo].[DeliveryBoys] db
        WHERE db.[PrimaryRouteId] = r.[RouteId] AND db.[IsActive] = 1
    ) db_counts
    OUTER APPLY (
        SELECT COUNT(1) AS [ActiveCustomerCount]
        FROM [dbo].[Customers] c
        WHERE c.[RouteId] = r.[RouteId] AND c.[IsActive] = 1
    ) cust_counts
    ORDER BY r.[IsActive] DESC, r.[RouteName] ASC;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Routes_GetAll] TO [db_dms_app];
GO

-- 3.2 Create New Route
CREATE OR ALTER PROCEDURE [dbo].[usp_Routes_Create]
    @RouteCode NVARCHAR(20),
    @RouteName NVARCHAR(100),
    @Description NVARCHAR(250) = NULL,
    @AdminUserId INT,
    @NewRouteId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RouteCode = UPPER(LTRIM(RTRIM(ISNULL(@RouteCode, N''))));
    SET @RouteName = LTRIM(RTRIM(ISNULL(@RouteName, N'')));
    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');

    IF (LEN(@RouteCode) = 0)
    BEGIN
        THROW 50060, 'Route code is required.', 1;
    END

    IF (LEN(@RouteName) = 0)
    BEGIN
        THROW 50061, 'Route name is required.', 1;
    END

    IF EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE UPPER([RouteCode]) = @RouteCode)
    BEGIN
        THROW 50062, 'A route with this code already exists.', 1;
    END

    IF EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE LOWER([RouteName]) = LOWER(@RouteName))
    BEGIN
        THROW 50063, 'A route with this name already exists.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO [dbo].[Routes] ([RouteCode], [RouteName], [Description], [IsActive], [CreatedAt], [CreatedBy])
        VALUES (@RouteCode, @RouteName, @Description, 1, SYSUTCDATETIME(), @AdminUserId);

        SET @NewRouteId = CAST(SCOPE_IDENTITY() AS INT);

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (
                @AdminUserId,
                'CREATE_ROUTE',
                'Routes',
                CAST(@NewRouteId AS VARCHAR(50)),
                CONCAT('{"RouteCode":"', REPLACE(@RouteCode, '"', '\"'), '","RouteName":"', REPLACE(@RouteName, '"', '\"'), '"}'),
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_Routes_Create] TO [db_dms_app];
GO

-- 3.3 Update Existing Route
CREATE OR ALTER PROCEDURE [dbo].[usp_Routes_Update]
    @RouteId INT,
    @RouteCode NVARCHAR(20),
    @RouteName NVARCHAR(100),
    @Description NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RouteCode = UPPER(LTRIM(RTRIM(ISNULL(@RouteCode, N''))));
    SET @RouteName = LTRIM(RTRIM(ISNULL(@RouteName, N'')));
    SET @Description = NULLIF(LTRIM(RTRIM(@Description)), N'');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE [RouteId] = @RouteId)
    BEGIN
        THROW 50064, 'Target route does not exist.', 1;
    END

    IF (LEN(@RouteCode) = 0)
    BEGIN
        THROW 50060, 'Route code is required.', 1;
    END

    IF (LEN(@RouteName) = 0)
    BEGIN
        THROW 50061, 'Route name is required.', 1;
    END

    IF EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE UPPER([RouteCode]) = @RouteCode AND [RouteId] <> @RouteId)
    BEGIN
        THROW 50062, 'Another route already uses this code.', 1;
    END

    IF EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE LOWER([RouteName]) = LOWER(@RouteName) AND [RouteId] <> @RouteId)
    BEGIN
        THROW 50063, 'Another route already uses this name.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE [dbo].[Routes]
        SET [RouteCode] = @RouteCode,
            [RouteName] = @RouteName,
            [Description] = @Description,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [RouteId] = @RouteId;

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (
                @AdminUserId,
                'UPDATE_ROUTE',
                'Routes',
                CAST(@RouteId AS VARCHAR(50)),
                CONCAT('{"RouteCode":"', REPLACE(@RouteCode, '"', '\"'), '","RouteName":"', REPLACE(@RouteName, '"', '\"'), '"}'),
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_Routes_Update] TO [db_dms_app];
GO

-- 3.4 Activate / Deactivate Route
CREATE OR ALTER PROCEDURE [dbo].[usp_Routes_SetActive]
    @RouteId INT,
    @IsActive BIT,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE [RouteId] = @RouteId)
    BEGIN
        THROW 50064, 'Target route does not exist.', 1;
    END

    IF (@IsActive = 0)
    BEGIN
        DECLARE @ActiveAgents INT = (SELECT COUNT(1) FROM [dbo].[DeliveryBoys] WHERE [PrimaryRouteId] = @RouteId AND [IsActive] = 1);
        IF (@ActiveAgents > 0)
        BEGIN
            THROW 50065, 'Cannot deactivate route while active delivery boy(s) are assigned to it. Reassign their primary route first.', 1;
        END

        DECLARE @ActiveCusts INT = (SELECT COUNT(1) FROM [dbo].[Customers] WHERE [RouteId] = @RouteId AND [IsActive] = 1);
        IF (@ActiveCusts > 0)
        BEGIN
            THROW 50066, 'Cannot deactivate route while active customer(s) are assigned to it. Transfer customers to another route first.', 1;
        END
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE [dbo].[Routes]
        SET [IsActive] = @IsActive,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [RouteId] = @RouteId;

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (
                @AdminUserId,
                CASE WHEN @IsActive = 1 THEN 'ACTIVATE_ROUTE' ELSE 'DEACTIVATE_ROUTE' END,
                'Routes',
                CAST(@RouteId AS VARCHAR(50)),
                CONCAT('{"IsActive":', CASE WHEN @IsActive = 1 THEN 'true' ELSE 'false' END, '}'),
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_Routes_SetActive] TO [db_dms_app];
GO

-- ------------------------------------------------------------------------------------
-- 4. Fix dbo.usp_Customer_ReassignDeliveryBoy (Handle Future-Dated Unstarted Assignments)
-- When a customer's active assignment has AssignedFrom > @Today (not yet started) and
-- no completed deliveries exist on/before @CurrentAssignedFrom, allow in-place correction
-- starting on @EffectiveDate (>= @Today and <= @CurrentAssignedFrom).
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Customer_ReassignDeliveryBoy]
    @CustomerId INT,
    @NewDeliveryBoyId INT = NULL, -- NULL represents unassignment
    @EffectiveDate DATE,
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    -- Case 5: Reject backdated assignment
    IF (@EffectiveDate < @Today)
    BEGIN
        THROW 50005, 'Backdated delivery boy reassignment is strictly prohibited. Historical delivery runsheets and commission statements are immutable.', 1;
    END

    -- Validate new delivery boy if provided
    IF (@NewDeliveryBoyId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[DeliveryBoys] WHERE [DeliveryBoyId] = @NewDeliveryBoyId AND [IsActive] = 1))
    BEGIN
        THROW 50008, 'Target delivery boy does not exist or is inactive.', 1;
    END

    BEGIN TRANSACTION;

    -- 1. Pessimistic Concurrency Lock on Customer Row
    SELECT 1 FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK) WHERE [CustomerId] = @CustomerId;

    -- 2. Fetch current active assignment
    DECLARE @ActiveAssignmentId BIGINT;
    DECLARE @CurrentDeliveryBoyId INT;
    DECLARE @CurrentAssignedFrom DATE;

    SELECT
        @ActiveAssignmentId = [AssignmentId],
        @CurrentDeliveryBoyId = [DeliveryBoyId],
        @CurrentAssignedFrom = [AssignedFrom]
    FROM [dbo].[CustomerDeliveryBoyAssignments] WITH (UPDLOCK)
    WHERE [CustomerId] = @CustomerId AND [AssignedTo] IS NULL AND [IsActive] = 1;

    -- Check if today's delivery has already been logged under current delivery boy
    DECLARE @DeliveryExistsToday BIT = 0;
    IF EXISTS (
        SELECT 1 FROM [dbo].[DailyMilkDeliveries]
        WHERE [CustomerId] = @CustomerId AND [EntryDate] = @Today AND [Status] IN ('Completed', 'Delivered')
    )
    BEGIN
        SET @DeliveryExistsToday = 1;
    END

    -- Case 3: Today's delivery already submitted and effective date is today
    IF (@DeliveryExistsToday = 1 AND @EffectiveDate = @Today)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50004, 'A delivery has already been recorded for this customer today under the assigned delivery boy. Reassignment cannot be applied to today. Transfer must take effect tomorrow.', 1;
    END

    -- Overlap Protection: EffectiveDate cannot overlap or precede any historical closed assignment
    IF EXISTS (
        SELECT 1 FROM [dbo].[CustomerDeliveryBoyAssignments]
        WHERE [CustomerId] = @CustomerId
          AND [AssignmentId] != ISNULL(@ActiveAssignmentId, -1)
          AND [AssignedTo] IS NOT NULL
          AND @EffectiveDate <= [AssignedTo]
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50005, 'Reassignment effective date conflicts with a previous historical assignment interval.', 1;
    END

    -- Case 1: In-place clerical correction
    -- Eligible when no delivery was recorded today AND either:
    --   (a) Assignment started today (@CurrentAssignedFrom = @EffectiveDate AND @EffectiveDate = @Today), OR
    --   (b) Assignment has a future start date (@CurrentAssignedFrom > @Today) and @EffectiveDate is between @Today and @CurrentAssignedFrom with no recorded deliveries in that window
    DECLARE @InPlaceEligible BIT = 0;
    IF (@ActiveAssignmentId IS NOT NULL AND @DeliveryExistsToday = 0)
    BEGIN
        IF (@CurrentAssignedFrom = @EffectiveDate AND @EffectiveDate = @Today)
        BEGIN
            SET @InPlaceEligible = 1;
        END
        ELSE IF (@CurrentAssignedFrom > @Today AND @EffectiveDate >= @Today AND @EffectiveDate <= @CurrentAssignedFrom)
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[DailyMilkDeliveries]
                WHERE [CustomerId] = @CustomerId
                  AND [EntryDate] >= @Today
                  AND [EntryDate] <= @CurrentAssignedFrom
                  AND [Status] IN ('Completed', 'Delivered')
            )
            BEGIN
                SET @InPlaceEligible = 1;
            END
        END
    END

    IF (@InPlaceEligible = 1)
    BEGIN
        IF (@NewDeliveryBoyId IS NOT NULL)
        BEGIN
            UPDATE [dbo].[CustomerDeliveryBoyAssignments]
            SET [DeliveryBoyId] = @NewDeliveryBoyId,
                [AssignedFrom] = @EffectiveDate,
                [Notes] = ISNULL([Notes] + ' | ', '') + 'Admin clerical correction before active deliveries',
                [UpdatedAt] = SYSUTCDATETIME(),
                [UpdatedBy] = @AdminUserId
            WHERE [AssignmentId] = @ActiveAssignmentId;
        END
        ELSE
        BEGIN
            UPDATE [dbo].[CustomerDeliveryBoyAssignments]
            SET [AssignedFrom] = @EffectiveDate,
                [AssignedTo] = @EffectiveDate,
                [IsActive] = 0,
                [Notes] = ISNULL([Notes] + ' | ', '') + 'Unassigned on/before start date without deliveries',
                [UpdatedAt] = SYSUTCDATETIME(),
                [UpdatedBy] = @AdminUserId
            WHERE [AssignmentId] = @ActiveAssignmentId;
        END

        UPDATE [dbo].[Customers]
        SET [CurrentDeliveryBoyId] = @NewDeliveryBoyId,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [CustomerId] = @CustomerId;

        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
            VALUES (@AdminUserId, 'REASSIGN_CLERICAL_CORRECTION_INPLACE', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
                    CONCAT('{"NewDeliveryBoyId":', ISNULL(CAST(@NewDeliveryBoyId AS VARCHAR(10)), 'null'), ',"EffectiveDate":"', CONVERT(VARCHAR(10), @EffectiveDate, 120), '"}'),
                    SYSUTCDATETIME());
        END

        COMMIT TRANSACTION;
        RETURN;
    END

    -- If EffectiveDate <= CurrentAssignedFrom and we didn't qualify for Case 1, reject
    IF (@ActiveAssignmentId IS NOT NULL AND @EffectiveDate <= @CurrentAssignedFrom)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50005, 'Reassignment effective date must be after the current assignment start date when in-place correction is not eligible.', 1;
    END

    -- Case 2: Standard Forward Reassignment (@EffectiveDate > @CurrentAssignedFrom)
    IF (@ActiveAssignmentId IS NOT NULL)
    BEGIN
        UPDATE [dbo].[CustomerDeliveryBoyAssignments]
        SET [AssignedTo] = DATEADD(day, -1, @EffectiveDate),
            [IsActive] = 0,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [AssignmentId] = @ActiveAssignmentId;
    END

    IF (@NewDeliveryBoyId IS NOT NULL)
    BEGIN
        INSERT INTO [dbo].[CustomerDeliveryBoyAssignments]
            ([CustomerId], [DeliveryBoyId], [AssignedFrom], [AssignedTo], [IsActive], [Notes], [CreatedAt], [CreatedBy])
        VALUES
            (@CustomerId, @NewDeliveryBoyId, @EffectiveDate, NULL, 1, @Notes, SYSUTCDATETIME(), @AdminUserId);
    END

    UPDATE [dbo].[Customers]
    SET [CurrentDeliveryBoyId] = @NewDeliveryBoyId,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId;

    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
    BEGIN
        INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
        VALUES (@AdminUserId, 'REASSIGN_DELIVERY_BOY_FORWARD', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
                CONCAT('{"NewDeliveryBoyId":', ISNULL(CAST(@NewDeliveryBoyId AS VARCHAR(10)), 'null'), ',"EffectiveDate":"', CONVERT(VARCHAR(10), @EffectiveDate, 120), '"}'),
                SYSUTCDATETIME());
    END

    COMMIT TRANSACTION;
END;
GO

GRANT EXECUTE ON [dbo].[usp_Customer_ReassignDeliveryBoy] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT '13_FixDeactivationAndRoutes.sql deployed successfully!';
PRINT '====================================================================================';
GO
