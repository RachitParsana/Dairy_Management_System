-- ====================================================================================
-- Script: 16_AdminUnlockDailyMilkBatch.sql
-- Description:
--   Adds Administrator capability to inspect shift batches and explicitly unlock/reopen
--   a submitted/locked Daily Milk runsheet back to 'Draft' status with mandatory audit
--   justification and strict financial period guards:
--     1. dbo.usp_DailyMilk_GetBatchesForAdmin
--     2. dbo.usp_DailyMilk_UnlockBatch
--   Blocks unlocking if any customer in the batch already has a Published monthly bill
--   covering that date, or if the Delivery Boy's monthly compensation statement for that
--   month is already Finalized or Paid.
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
PRINT 'Deploying 16_AdminUnlockDailyMilkBatch.sql on [' + DB_NAME() + ']...';
PRINT '====================================================================================';
GO

-- ------------------------------------------------------------------------------------
-- 1. Stored Procedure: dbo.usp_DailyMilk_GetBatchesForAdmin
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_GetBatchesForAdmin]
    @EntryDate DATE = NULL,
    @Shift NVARCHAR(20) = NULL,
    @DeliveryBoyId INT = NULL,
    @Status NVARCHAR(20) = NULL,
    @Limit INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    IF (@Limit IS NULL OR @Limit <= 0) SET @Limit = 50;
    IF (@Limit > 200) SET @Limit = 200;

    SELECT TOP (@Limit)
        b.[BatchId],
        b.[DeliveryBoyId],
        dbUser.[FullName] AS DeliveryBoyName,
        dbUser.[Phone] AS DeliveryBoyPhone,
        b.[RouteId],
        r.[RouteName],
        b.[EntryDate],
        b.[Shift],
        b.[Status],
        b.[TotalAssignedCustomers],
        b.[CompletedCount],
        b.[PendingCount],
        b.[NotDeliveredCount],
        b.[TotalLiquidVolumeLitres],
        b.[TotalAmount],
        b.[SubmittedAt],
        subUser.[FullName] AS SubmittedByName,
        CASE WHEN EXISTS (
            SELECT 1
            FROM [dbo].[DailyMilkDeliveries] d
            INNER JOIN [dbo].[MonthlyBills] mb ON mb.[CustomerId] = d.[CustomerId]
            WHERE (d.[BatchId] = b.[BatchId] OR (d.[DeliveryBoyId] = b.[DeliveryBoyId] AND d.[EntryDate] = b.[EntryDate] AND d.[Shift] = b.[Shift]))
              AND mb.[IsPublished] = 1
              AND b.[EntryDate] BETWEEN mb.[PeriodStartDate] AND mb.[PeriodEndDate]
              AND ISNULL(mb.[PublishedAt], mb.[CreatedAt]) >= ISNULL(b.[SubmittedAt], b.[CreatedAt])
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasPublishedBillLock,
        CASE WHEN EXISTS (
            SELECT 1
            FROM [dbo].[DeliveryBoyMonthlyCompensations] comp
            WHERE comp.[DeliveryBoyId] = b.[DeliveryBoyId]
              AND comp.[Month] = MONTH(b.[EntryDate])
              AND comp.[Year] = YEAR(b.[EntryDate])
              AND comp.[Status] IN (N'Finalized', N'Paid')
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasFinalizedCompensationLock
    FROM [dbo].[DailyDeliveryBatches] b
    INNER JOIN [dbo].[DeliveryBoys] db ON b.[DeliveryBoyId] = db.[DeliveryBoyId]
    INNER JOIN [dbo].[Users] dbUser ON db.[UserId] = dbUser.[UserId]
    INNER JOIN [dbo].[Routes] r ON b.[RouteId] = r.[RouteId]
    LEFT JOIN [dbo].[Users] subUser ON b.[SubmittedBy] = subUser.[UserId]
    WHERE (@EntryDate IS NULL OR b.[EntryDate] = @EntryDate)
      AND (@Shift IS NULL OR @Shift = N'' OR b.[Shift] = @Shift)
      AND (@DeliveryBoyId IS NULL OR b.[DeliveryBoyId] = @DeliveryBoyId)
      AND (@Status IS NULL OR @Status = N'' OR b.[Status] = @Status)
    ORDER BY b.[EntryDate] DESC,
             CASE b.[Shift] WHEN N'Evening' THEN 2 WHEN N'Morning' THEN 1 ELSE 0 END DESC,
             dbUser.[FullName] ASC,
             b.[BatchId] DESC;
END;
GO

-- ------------------------------------------------------------------------------------
-- 2. Stored Procedure: dbo.usp_DailyMilk_UnlockBatch
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_UnlockBatch]
    @BatchId BIGINT,
    @UnlockReason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TrimmedReason NVARCHAR(250) = LTRIM(RTRIM(ISNULL(@UnlockReason, N'')));
    IF (LEN(@TrimmedReason) < 5)
    BEGIN
        THROW 50041, 'Unlock justification must be at least 5 characters long.', 1;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM [dbo].[Users]
        WHERE [UserId] = @AdminUserId
          AND [Role] = N'Admin'
          AND [IsActive] = 1
    )
    BEGIN
        THROW 50042, 'Only an active Administrator can unlock a finalized delivery batch.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @DeliveryBoyId INT, @EntryDate DATE, @Shift NVARCHAR(20), @CurrentStatus NVARCHAR(20);
        DECLARE @SubmittedAt DATETIME2(7), @SubmittedBy INT;

        SELECT
            @DeliveryBoyId = [DeliveryBoyId],
            @EntryDate = [EntryDate],
            @Shift = [Shift],
            @CurrentStatus = [Status],
            @SubmittedAt = [SubmittedAt],
            @SubmittedBy = [SubmittedBy]
        FROM [dbo].[DailyDeliveryBatches] WITH (UPDLOCK, HOLDLOCK)
        WHERE [BatchId] = @BatchId;

        IF (@DeliveryBoyId IS NULL)
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50043, 'Delivery batch record does not exist.', 1;
        END

        IF (@CurrentStatus <> N'Submitted')
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50044, 'This delivery batch is already unlocked (Draft status).', 1;
        END

        -- Guard 1: Block unlocking if any customer in this batch had a bill published at/after submission covering EntryDate
        IF EXISTS (
            SELECT 1
            FROM [dbo].[DailyMilkDeliveries] d
            INNER JOIN [dbo].[MonthlyBills] mb ON mb.[CustomerId] = d.[CustomerId]
            WHERE (d.[BatchId] = @BatchId OR (d.[DeliveryBoyId] = @DeliveryBoyId AND d.[EntryDate] = @EntryDate AND d.[Shift] = @Shift))
              AND mb.[IsPublished] = 1
              AND @EntryDate BETWEEN mb.[PeriodStartDate] AND mb.[PeriodEndDate]
              AND ISNULL(mb.[PublishedAt], mb.[CreatedAt]) >= COALESCE(@SubmittedAt, mb.[PublishedAt], mb.[CreatedAt])
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50045, 'Cannot unlock shift: One or more customers in this batch already have a Published monthly bill covering this date. Use Bill Revision instead.', 1;
        END

        -- Guard 2: Block unlocking if the Delivery Boy's monthly compensation statement is already Finalized or Paid
        IF EXISTS (
            SELECT 1
            FROM [dbo].[DeliveryBoyMonthlyCompensations] comp
            WHERE comp.[DeliveryBoyId] = @DeliveryBoyId
              AND comp.[Month] = MONTH(@EntryDate)
              AND comp.[Year] = YEAR(@EntryDate)
              AND comp.[Status] IN (N'Finalized', N'Paid')
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50046, 'Cannot unlock shift: Monthly compensation statement for this delivery partner and month has already been Finalized or Paid.', 1;
        END

        -- Revert batch header from 'Submitted' back to 'Draft' preserving all saved customer entries
        UPDATE [dbo].[DailyDeliveryBatches]
        SET [Status] = N'Draft',
            [SubmittedAt] = NULL,
            [SubmittedBy] = NULL,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [BatchId] = @BatchId;

        -- Immutable audit trail entry
        INSERT INTO [dbo].[AuditLogs] (
            [UserId], [Action], [TableName], [RecordId], [OldValuesJson], [NewValuesJson], [IpAddress], [Timestamp]
        )
        VALUES (
            @AdminUserId,
            'UNLOCK_BATCH',
            'DailyDeliveryBatches',
            CAST(@BatchId AS VARCHAR(50)),
            CONCAT(
                N'{"BatchId":', @BatchId,
                N',"DeliveryBoyId":', @DeliveryBoyId,
                N',"EntryDate":"', CONVERT(NVARCHAR(10), @EntryDate, 23),
                N'","Shift":"', @Shift,
                N'","Status":"Submitted"}'
            ),
            CONCAT(
                N'{"BatchId":', @BatchId,
                N',"Status":"Draft","UnlockReason":"', STRING_ESCAPE(@TrimmedReason, 'json'), N'"}'
            ),
            NULL,
            SYSUTCDATETIME()
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON [dbo].[usp_DailyMilk_GetBatchesForAdmin] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DailyMilk_UnlockBatch] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT 'Completed 16_AdminUnlockDailyMilkBatch.sql.';
PRINT '====================================================================================';
GO
