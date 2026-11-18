-- ====================================================================================
-- Dairy Management System — Day 4 Database Engineering
-- Script 07: Daily Milk Management Schema, TVPs, Stored Procedures & Security
-- Engineer: Kevin Gajera
-- Date: 18-Nov-2026
-- Description: Production-grade, idempotent migration for Daily Milk Management.
-- ====================================================================================

USE [DairyManagementDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting 07_DailyMilkManagement migration...';
PRINT '====================================================================================';
GO

-- ====================================================================================
-- 1. Table: dbo.DailyDeliveryBatches
-- Represents a delivery runsheet session for a delivery boy on a specific date/shift.
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DailyDeliveryBatches')
BEGIN
    PRINT 'Creating table dbo.DailyDeliveryBatches...';
    CREATE TABLE [dbo].[DailyDeliveryBatches] (
        [BatchId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DailyDeliveryBatches] PRIMARY KEY CLUSTERED,
        [DeliveryBoyId] INT NOT NULL CONSTRAINT [FK_DailyDeliveryBatches_DeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [RouteId] INT NOT NULL CONSTRAINT [FK_DailyDeliveryBatches_RouteId] REFERENCES [dbo].[Routes]([RouteId]),
        [EntryDate] DATE NOT NULL,
        [Shift] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DailyDeliveryBatches_Shift] CHECK ([Shift] IN ('Morning', 'Evening')),
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_Status] DEFAULT ('Draft') CONSTRAINT [CK_DailyDeliveryBatches_Status] CHECK ([Status] IN ('Draft', 'Submitted')),
        [TotalAssignedCustomers] INT NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_TotalAssigned] DEFAULT (0) CONSTRAINT [CK_DailyDeliveryBatches_TotalAssigned] CHECK ([TotalAssignedCustomers] >= 0),
        [CompletedCount] INT NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_Completed] DEFAULT (0) CONSTRAINT [CK_DailyDeliveryBatches_Completed] CHECK ([CompletedCount] >= 0),
        [PendingCount] INT NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_Pending] DEFAULT (0) CONSTRAINT [CK_DailyDeliveryBatches_Pending] CHECK ([PendingCount] >= 0),
        [NotDeliveredCount] INT NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_NotDelivered] DEFAULT (0) CONSTRAINT [CK_DailyDeliveryBatches_NotDelivered] CHECK ([NotDeliveredCount] >= 0),
        [TotalLiquidVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_DailyDeliveryBatches_LiquidVolume] CHECK ([TotalLiquidVolumeLitres] >= 0),
        [TotalAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_TotalAmount] DEFAULT (0.00) CONSTRAINT [CK_DailyDeliveryBatches_TotalAmount] CHECK ([TotalAmount] >= 0),
        [StartedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_StartedAt] DEFAULT (SYSUTCDATETIME()),
        [SubmittedAt] DATETIME2(7) NULL,
        [SubmittedBy] INT NULL CONSTRAINT [FK_DailyDeliveryBatches_SubmittedBy] REFERENCES [dbo].[Users]([UserId]),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DailyDeliveryBatches_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DailyDeliveryBatches_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_DailyDeliveryBatches_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_DailyDeliveryBatches_DeliveryBoy_Date_Shift' AND object_id = OBJECT_ID(N'[dbo].[DailyDeliveryBatches]'))
BEGIN
    PRINT 'Creating index UQ_DailyDeliveryBatches_DeliveryBoy_Date_Shift...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_DailyDeliveryBatches_DeliveryBoy_Date_Shift]
    ON [dbo].[DailyDeliveryBatches]([DeliveryBoyId], [EntryDate], [Shift]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyDeliveryBatches_Date_Shift_Status' AND object_id = OBJECT_ID(N'[dbo].[DailyDeliveryBatches]'))
BEGIN
    PRINT 'Creating index IX_DailyDeliveryBatches_Date_Shift_Status...';
    CREATE NONCLUSTERED INDEX [IX_DailyDeliveryBatches_Date_Shift_Status]
    ON [dbo].[DailyDeliveryBatches]([EntryDate], [Shift], [Status])
    INCLUDE ([RouteId], [TotalLiquidVolumeLitres], [TotalAmount]);
END
GO

-- ====================================================================================
-- 2. Table: dbo.CustomerUnscheduledDeliveryAuthorizations (Parent Exception Grant)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerUnscheduledDeliveryAuthorizations')
BEGIN
    PRINT 'Creating table dbo.CustomerUnscheduledDeliveryAuthorizations...';
    CREATE TABLE [dbo].[CustomerUnscheduledDeliveryAuthorizations] (
        [AuthorizationId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerUnscheduledDeliveryAuthorizations] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_UnscheduledAuth_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [CategoryId] INT NULL CONSTRAINT [FK_UnscheduledAuth_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [EntryDate] DATE NOT NULL,
        [AuthorizedShift] NVARCHAR(20) NOT NULL CONSTRAINT [CK_UnscheduledAuth_Shift] CHECK ([AuthorizedShift] IN ('Morning', 'Evening', 'Both')),
        [MaxQuantity] DECIMAL(18,3) NULL CONSTRAINT [CK_UnscheduledAuth_MaxQuantity] CHECK ([MaxQuantity] IS NULL OR [MaxQuantity] > 0),
        [Reason] NVARCHAR(250) NOT NULL,
        [AuthorizedBy] INT NOT NULL CONSTRAINT [FK_UnscheduledAuth_AuthorizedBy] REFERENCES [dbo].[Users]([UserId]),
        [AuthorizedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_UnscheduledAuth_AuthorizedAt] DEFAULT (SYSUTCDATETIME()),
        [IsRevoked] BIT NOT NULL CONSTRAINT [DF_UnscheduledAuth_IsRevoked] DEFAULT (0),
        [RevokedBy] INT NULL CONSTRAINT [FK_UnscheduledAuth_RevokedBy] REFERENCES [dbo].[Users]([UserId]),
        [RevokedAt] DATETIME2(7) NULL,
        [RevokeReason] NVARCHAR(250) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UnscheduledAuth_Customer_Date' AND object_id = OBJECT_ID(N'[dbo].[CustomerUnscheduledDeliveryAuthorizations]'))
BEGIN
    PRINT 'Creating index IX_UnscheduledAuth_Customer_Date...';
    CREATE NONCLUSTERED INDEX [IX_UnscheduledAuth_Customer_Date]
    ON [dbo].[CustomerUnscheduledDeliveryAuthorizations]([CustomerId], [EntryDate], [AuthorizedShift])
    INCLUDE ([AuthorizedBy], [CategoryId], [MaxQuantity])
    WHERE [IsRevoked] = 0;
END
GO

-- ====================================================================================
-- 3. Table: dbo.DailyMilkDeliveries (Additive Schema Augmentations & Indexes)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]') AND name = N'BatchId')
BEGIN
    PRINT 'Augmenting dbo.DailyMilkDeliveries with BatchId...';
    ALTER TABLE [dbo].[DailyMilkDeliveries]
    ADD [BatchId] BIGINT NULL CONSTRAINT [FK_DailyMilkDeliveries_BatchId] REFERENCES [dbo].[DailyDeliveryBatches]([BatchId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]') AND name = N'IsUnscheduledDelivery')
BEGIN
    PRINT 'Augmenting dbo.DailyMilkDeliveries with IsUnscheduledDelivery...';
    ALTER TABLE [dbo].[DailyMilkDeliveries]
    ADD [IsUnscheduledDelivery] BIT NOT NULL CONSTRAINT [DF_DailyMilkDeliveries_IsUnscheduled] DEFAULT (0);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]') AND name = N'AuthorizationId')
BEGIN
    PRINT 'Augmenting dbo.DailyMilkDeliveries with AuthorizationId...';
    ALTER TABLE [dbo].[DailyMilkDeliveries]
    ADD [AuthorizationId] BIGINT NULL CONSTRAINT [FK_DailyMilkDeliveries_AuthId] REFERENCES [dbo].[CustomerUnscheduledDeliveryAuthorizations]([AuthorizationId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]') AND name = N'AdminAuthorizedBy')
BEGIN
    PRINT 'Augmenting dbo.DailyMilkDeliveries with AdminAuthorizedBy...';
    ALTER TABLE [dbo].[DailyMilkDeliveries]
    ADD [AdminAuthorizedBy] INT NULL CONSTRAINT [FK_DailyMilkDeliveries_AdminAuth] REFERENCES [dbo].[Users]([UserId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]') AND name = N'IsCorrected')
BEGIN
    PRINT 'Augmenting dbo.DailyMilkDeliveries with IsCorrected and audit metadata...';
    ALTER TABLE [dbo].[DailyMilkDeliveries]
    ADD [IsCorrected] BIT NOT NULL CONSTRAINT [DF_DailyMilkDeliveries_IsCorrected] DEFAULT (0),
        [LastCorrectedAt] DATETIME2(7) NULL,
        [LastCorrectedBy] INT NULL CONSTRAINT [FK_DailyMilkDeliveries_LastCorrectedBy] REFERENCES [dbo].[Users]([UserId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyMilkDeliveries_BatchId' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]'))
BEGIN
    PRINT 'Creating index IX_DailyMilkDeliveries_BatchId...';
    CREATE NONCLUSTERED INDEX [IX_DailyMilkDeliveries_BatchId]
    ON [dbo].[DailyMilkDeliveries]([BatchId]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyMilkDeliveries_ContinuousScroll' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]'))
BEGIN
    PRINT 'Creating composite cursor index IX_DailyMilkDeliveries_ContinuousScroll...';
    CREATE NONCLUSTERED INDEX [IX_DailyMilkDeliveries_ContinuousScroll]
    ON [dbo].[DailyMilkDeliveries]([EntryDate] DESC, [Shift] DESC, [DeliveryId] DESC)
    INCLUDE ([CustomerId], [DeliveryBoyId], [Status], [TotalLiquidVolumeLitres], [TotalAmount], [IsCorrected]);
END
GO

-- ====================================================================================
-- 4. Table: dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions (Normalized Child)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerUnscheduledDeliveryAuthorizationConsumptions')
BEGIN
    PRINT 'Creating table dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions...';
    CREATE TABLE [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] (
        [ConsumptionId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerUnscheduledDeliveryAuthorizationConsumptions] PRIMARY KEY CLUSTERED,
        [AuthorizationId] BIGINT NOT NULL CONSTRAINT [FK_UnscheduledConsumption_AuthId] REFERENCES [dbo].[CustomerUnscheduledDeliveryAuthorizations]([AuthorizationId]),
        [DeliveryId] BIGINT NOT NULL CONSTRAINT [FK_UnscheduledConsumption_DeliveryId] REFERENCES [dbo].[DailyMilkDeliveries]([DeliveryId]),
        [ConsumedShift] NVARCHAR(20) NOT NULL CONSTRAINT [CK_UnscheduledConsumption_Shift] CHECK ([ConsumedShift] IN ('Morning', 'Evening')),
        [ConsumedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_UnscheduledConsumption_ConsumedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_UnscheduledAuth_Consumption_Shift' AND object_id = OBJECT_ID(N'[dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions]'))
BEGIN
    PRINT 'Creating index UQ_UnscheduledAuth_Consumption_Shift...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_UnscheduledAuth_Consumption_Shift]
    ON [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions]([AuthorizationId], [ConsumedShift]);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_UnscheduledAuth_Delivery' AND object_id = OBJECT_ID(N'[dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions]'))
BEGIN
    PRINT 'Creating index UQ_UnscheduledAuth_Delivery...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_UnscheduledAuth_Delivery]
    ON [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions]([DeliveryId]);
END
GO

-- ====================================================================================
-- 5. Table: dbo.DailyMilkDeliveryCorrections (Append-Only Audit Ledger)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DailyMilkDeliveryCorrections')
BEGIN
    PRINT 'Creating table dbo.DailyMilkDeliveryCorrections...';
    CREATE TABLE [dbo].[DailyMilkDeliveryCorrections] (
        [CorrectionId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DailyMilkDeliveryCorrections] PRIMARY KEY CLUSTERED,
        [CorrectionBatchGuid] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_DailyMilkDeliveryCorrections_Guid] DEFAULT (NEWID()),
        [DeliveryId] BIGINT NOT NULL CONSTRAINT [FK_Corrections_DeliveryId] REFERENCES [dbo].[DailyMilkDeliveries]([DeliveryId]),
        [DeliveryItemId] BIGINT NULL CONSTRAINT [FK_Corrections_DeliveryItemId] REFERENCES [dbo].[DailyMilkDeliveryItems]([DeliveryItemId]),
        [CustomerId] INT NOT NULL CONSTRAINT [FK_Corrections_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [EntryDate] DATE NOT NULL,
        [Shift] NVARCHAR(20) NOT NULL,
        [PriorStatus] NVARCHAR(20) NOT NULL,
        [NewStatus] NVARCHAR(20) NOT NULL,
        [CategoryId] INT NULL CONSTRAINT [FK_Corrections_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [PriorPackagingQuantity] DECIMAL(18,3) NULL,
        [NewPackagingQuantity] DECIMAL(18,3) NULL,
        [PriorLiquidVolumeLitres] DECIMAL(18,3) NOT NULL,
        [NewLiquidVolumeLitres] DECIMAL(18,3) NOT NULL,
        [PriorUnitPrice] DECIMAL(18,2) NULL,
        [NewUnitPrice] DECIMAL(18,2) NULL,
        [PriorTotalAmount] DECIMAL(18,2) NOT NULL,
        [NewTotalAmount] DECIMAL(18,2) NOT NULL,
        [Reason] NVARCHAR(250) NOT NULL,
        [CorrectedBy] INT NOT NULL CONSTRAINT [FK_Corrections_CorrectedBy] REFERENCES [dbo].[Users]([UserId]),
        [CorrectedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Corrections_CorrectedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Corrections_DeliveryId' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveryCorrections]'))
BEGIN
    PRINT 'Creating index IX_Corrections_DeliveryId...';
    CREATE NONCLUSTERED INDEX [IX_Corrections_DeliveryId]
    ON [dbo].[DailyMilkDeliveryCorrections]([DeliveryId], [CorrectedAt] DESC);
END
GO

-- Trigger: trg_DailyMilkDeliveryCorrections_PreventModification (Enforces append-only immutable audit)
CREATE OR ALTER TRIGGER [dbo].[trg_DailyMilkDeliveryCorrections_PreventModification]
ON [dbo].[DailyMilkDeliveryCorrections]
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('DailyMilkDeliveryCorrections is an immutable append-only audit ledger. Updates and deletions are strictly prohibited.', 16, 1);
    ROLLBACK TRANSACTION;
END;
GO

-- ====================================================================================
-- 6. Table: dbo.DeliveryBoyCommissionRates (Augmentation with Notes & Constraints)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[DeliveryBoyCommissionRates]') AND name = N'Notes')
BEGIN
    PRINT 'Augmenting dbo.DeliveryBoyCommissionRates with Notes column...';
    ALTER TABLE [dbo].[DeliveryBoyCommissionRates]
    ADD [Notes] NVARCHAR(250) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryBoyCommissionRates_Resolution' AND object_id = OBJECT_ID(N'[dbo].[DeliveryBoyCommissionRates]'))
BEGIN
    PRINT 'Creating index IX_DeliveryBoyCommissionRates_Resolution...';
    CREATE NONCLUSTERED INDEX [IX_DeliveryBoyCommissionRates_Resolution]
    ON [dbo].[DeliveryBoyCommissionRates]([DeliveryBoyId], [EffectiveFrom], [EffectiveTo])
    INCLUDE ([CommissionRatePerLitre]);
END
GO

-- Trigger: trg_DeliveryBoyCommissionRates_PreventOverlap (Secondary Defensive Guard)
CREATE OR ALTER TRIGGER [dbo].[trg_DeliveryBoyCommissionRates_PreventOverlap]
ON [dbo].[DeliveryBoyCommissionRates]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Identify all affected DeliveryBoyIds from both inserted and deleted (handles reassignments)
    DECLARE @TargetDeliveryBoys TABLE (
        [DeliveryBoyId] INT NOT NULL PRIMARY KEY CLUSTERED
    );

    INSERT INTO @TargetDeliveryBoys ([DeliveryBoyId])
    SELECT DISTINCT [DeliveryBoyId] FROM [inserted]
    UNION
    SELECT DISTINCT [DeliveryBoyId] FROM [deleted];

    IF NOT EXISTS (SELECT 1 FROM @TargetDeliveryBoys)
        RETURN;

    -- 2. Concurrency-Safe Serialization via Deterministic Ascending Lock Ordering
    DECLARE @LockDeliveryBoyId INT;
    DECLARE @LockAcquired INT;
    DECLARE curDeliveryBoyLocks CURSOR LOCAL FAST_FORWARD FOR
        SELECT [DeliveryBoyId]
        FROM @TargetDeliveryBoys
        ORDER BY [DeliveryBoyId] ASC;

    OPEN curDeliveryBoyLocks;
    FETCH NEXT FROM curDeliveryBoyLocks INTO @LockDeliveryBoyId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @LockAcquired = [DeliveryBoyId]
        FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
        WHERE [DeliveryBoyId] = @LockDeliveryBoyId;

        FETCH NEXT FROM curDeliveryBoyLocks INTO @LockDeliveryBoyId;
    END

    CLOSE curDeliveryBoyLocks;
    DEALLOCATE curDeliveryBoyLocks;

    -- 3. Multi-Row Check 1: Detect collisions within the inserted batch itself (Intra-batch overlap)
    DECLARE @IntraOverlapBoyId INT;
    SELECT TOP 1 @IntraOverlapBoyId = i1.DeliveryBoyId
    FROM inserted i1
    JOIN inserted i2 ON i1.DeliveryBoyId = i2.DeliveryBoyId
    WHERE (i1.RateId < i2.RateId OR (i1.RateId = 0 AND i2.RateId = 0 AND i1.EffectiveFrom <> i2.EffectiveFrom))
      AND (i1.EffectiveTo IS NULL OR i2.EffectiveFrom <= i1.EffectiveTo)
      AND (i2.EffectiveTo IS NULL OR i2.EffectiveTo >= i1.EffectiveFrom);

    IF (@IntraOverlapBoyId IS NOT NULL)
    BEGIN
        RAISERROR('Cannot insert multiple overlapping commission rate tiers for delivery boy ID %d within a single batch.', 16, 1, @IntraOverlapBoyId);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- 4. Multi-Row Check 2: Detect collisions between inserted/updated rows and existing committed tiers
    DECLARE @InterOverlapBoyId INT;
    SELECT TOP 1 @InterOverlapBoyId = i.DeliveryBoyId
    FROM dbo.DeliveryBoyCommissionRates r
    JOIN inserted i ON r.DeliveryBoyId = i.DeliveryBoyId
    WHERE r.RateId <> i.RateId -- Exclude self during UPDATE
      AND (i.EffectiveTo IS NULL OR r.EffectiveFrom <= i.EffectiveTo)
      AND (r.EffectiveTo IS NULL OR r.EffectiveTo >= i.EffectiveFrom);

    IF (@InterOverlapBoyId IS NOT NULL)
    BEGIN
        RAISERROR('Cannot insert or update commission rate tier: Effective date range overlaps with an existing rate tier for delivery boy ID %d.', 16, 1, @InterOverlapBoyId);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

-- ====================================================================================
-- 7. Table-Valued Parameter Types
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.table_types WHERE name = N'DailyMilkDeliveryItemInputType')
BEGIN
    PRINT 'Creating table type dbo.DailyMilkDeliveryItemInputType...';
    CREATE TYPE [dbo].[DailyMilkDeliveryItemInputType] AS TABLE (
        [CustomerId] INT NOT NULL,
        [CategoryId] INT NOT NULL,
        [PackagingQuantity] DECIMAL(18,3) NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.table_types WHERE name = N'DailyMilkDeliveryHeaderInputType')
BEGIN
    PRINT 'Creating table type dbo.DailyMilkDeliveryHeaderInputType...';
    CREATE TYPE [dbo].[DailyMilkDeliveryHeaderInputType] AS TABLE (
        [CustomerId] INT NOT NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [NonDeliveryReason] NVARCHAR(250) NULL,
        [IsUnscheduledDelivery] BIT NOT NULL DEFAULT (0)
    );
END
GO

-- ====================================================================================
-- 8. Stored Procedures: Commission Rate Management (Primary Concurrency Guard)
-- ====================================================================================

-- Stored Procedure: usp_DeliveryBoy_CreateCommissionRate
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_CreateCommissionRate]
    @DeliveryBoyId INT,
    @CommissionRate DECIMAL(18,2),
    @EffectiveFrom DATE,
    @EffectiveTo DATE = NULL,
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT,
    @RateId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@CommissionRate < 0)
    BEGIN
        THROW 50036, 'Commission rate must be non-negative.', 1;
    END

    IF (@EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom)
    BEGIN
        THROW 50036, 'EffectiveTo date cannot be earlier than EffectiveFrom date.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Identify all affected DeliveryBoyIds
        DECLARE @TargetDeliveryBoys TABLE (
            [DeliveryBoyId] INT NOT NULL PRIMARY KEY CLUSTERED
        );
        INSERT INTO @TargetDeliveryBoys ([DeliveryBoyId]) VALUES (@DeliveryBoyId);

        -- 2. Acquire UPDLOCK and HOLDLOCK on parent DeliveryBoys in deterministic ASCENDING order
        DECLARE @LockDeliveryBoyId INT;
        DECLARE @LockAcquired INT;
        DECLARE curProcLocks CURSOR LOCAL FAST_FORWARD FOR
            SELECT [DeliveryBoyId]
            FROM @TargetDeliveryBoys
            ORDER BY [DeliveryBoyId] ASC;

        OPEN curProcLocks;
        FETCH NEXT FROM curProcLocks INTO @LockDeliveryBoyId;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SELECT @LockAcquired = [DeliveryBoyId]
            FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
            WHERE [DeliveryBoyId] = @LockDeliveryBoyId;

            FETCH NEXT FROM curProcLocks INTO @LockDeliveryBoyId;
        END

        CLOSE curProcLocks;
        DEALLOCATE curProcLocks;

        -- 3. Perform Pre-Insert Overlap Validation against committed tiers
        IF EXISTS (
            SELECT 1
            FROM dbo.DeliveryBoyCommissionRates
            WHERE DeliveryBoyId = @DeliveryBoyId
              AND (@EffectiveTo IS NULL OR EffectiveFrom <= @EffectiveTo)
              AND (EffectiveTo IS NULL OR EffectiveTo >= @EffectiveFrom)
        )
        BEGIN
            -- If an existing open-ended tier exists and new rate is strictly newer, close the prior tier
            DECLARE @PriorOpenEndedRateId INT, @PriorEffectiveFrom DATE;
            SELECT @PriorOpenEndedRateId = RateId, @PriorEffectiveFrom = EffectiveFrom
            FROM dbo.DeliveryBoyCommissionRates
            WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveTo IS NULL;

            IF (@PriorOpenEndedRateId IS NOT NULL AND @EffectiveFrom > @PriorEffectiveFrom AND @EffectiveTo IS NULL)
            BEGIN
                UPDATE dbo.DeliveryBoyCommissionRates
                SET EffectiveTo = DATEADD(day, -1, @EffectiveFrom)
                WHERE RateId = @PriorOpenEndedRateId;
            END
            ELSE
            BEGIN
                THROW 50036, 'Cannot create commission rate tier: Effective date range overlaps with an existing rate tier for delivery boy.', 1;
            END
        END

        -- 4. Execute the INSERT only after parent locks are acquired and validation succeeds
        INSERT INTO dbo.DeliveryBoyCommissionRates (
            DeliveryBoyId, CommissionRatePerLitre, EffectiveFrom, EffectiveTo, Notes, CreatedBy, CreatedAt
        )
        VALUES (
            @DeliveryBoyId, @CommissionRate, @EffectiveFrom, @EffectiveTo, @Notes, @AdminUserId, SYSUTCDATETIME()
        );

        SET @RateId = SCOPE_IDENTITY();

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- Stored Procedure: usp_DeliveryBoy_UpdateCommissionRate
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_UpdateCommissionRate]
    @RateId INT,
    @DeliveryBoyId INT,
    @CommissionRate DECIMAL(18,2),
    @EffectiveFrom DATE,
    @EffectiveTo DATE = NULL,
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@CommissionRate < 0)
    BEGIN
        THROW 50036, 'Commission rate must be non-negative.', 1;
    END

    IF (@EffectiveTo IS NOT NULL AND @EffectiveTo < @EffectiveFrom)
    BEGIN
        THROW 50036, 'EffectiveTo date cannot be earlier than EffectiveFrom date.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Identify old and new DeliveryBoyIds for reassignment locking
        DECLARE @OldDeliveryBoyId INT;
        DECLARE @OldEffectiveFrom DATE;
        DECLARE @OldEffectiveTo DATE;
        SELECT 
            @OldDeliveryBoyId = DeliveryBoyId,
            @OldEffectiveFrom = EffectiveFrom,
            @OldEffectiveTo = EffectiveTo
        FROM dbo.DeliveryBoyCommissionRates
        WHERE RateId = @RateId;

        IF (@OldDeliveryBoyId IS NULL)
        BEGIN
            THROW 50036, 'Commission rate record does not exist.', 1;
        END

        -- 1b. Immutability Guard: Check if this rate tier has already been used by finalized delivery batches
        IF EXISTS (
            SELECT 1
            FROM dbo.DailyDeliveryBatches b
            WHERE b.DeliveryBoyId = @OldDeliveryBoyId
              AND b.Status = 'Submitted'
              AND b.EntryDate >= @OldEffectiveFrom
              AND (@OldEffectiveTo IS NULL OR b.EntryDate <= @OldEffectiveTo)
        )
        BEGIN
            THROW 50037, 'Cannot modify commission rate tier: This rate tier has already been used by finalized delivery batches. Historical commission rate tiers are immutable.', 1;
        END

        DECLARE @TargetDeliveryBoys TABLE (
            [DeliveryBoyId] INT NOT NULL PRIMARY KEY CLUSTERED
        );
        INSERT INTO @TargetDeliveryBoys ([DeliveryBoyId]) VALUES (@OldDeliveryBoyId);
        IF (@DeliveryBoyId <> @OldDeliveryBoyId)
        BEGIN
            INSERT INTO @TargetDeliveryBoys ([DeliveryBoyId]) VALUES (@DeliveryBoyId);
        END

        -- 2. Acquire parent locks in deterministic ascending order
        DECLARE @LockDeliveryBoyId INT;
        DECLARE @LockAcquired INT;
        DECLARE curUpdateLocks CURSOR LOCAL FAST_FORWARD FOR
            SELECT [DeliveryBoyId]
            FROM @TargetDeliveryBoys
            ORDER BY [DeliveryBoyId] ASC;

        OPEN curUpdateLocks;
        FETCH NEXT FROM curUpdateLocks INTO @LockDeliveryBoyId;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SELECT @LockAcquired = [DeliveryBoyId]
            FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
            WHERE [DeliveryBoyId] = @LockDeliveryBoyId;

            FETCH NEXT FROM curUpdateLocks INTO @LockDeliveryBoyId;
        END

        CLOSE curUpdateLocks;
        DEALLOCATE curUpdateLocks;

        -- 3. Overlap validation against other tiers
        IF EXISTS (
            SELECT 1
            FROM dbo.DeliveryBoyCommissionRates
            WHERE DeliveryBoyId = @DeliveryBoyId
              AND RateId <> @RateId
              AND (@EffectiveTo IS NULL OR EffectiveFrom <= @EffectiveTo)
              AND (EffectiveTo IS NULL OR EffectiveTo >= @EffectiveFrom)
        )
        BEGIN
            THROW 50036, 'Cannot update commission rate tier: Effective date range overlaps with another existing rate tier.', 1;
        END

        -- 4. Update the record
        UPDATE dbo.DeliveryBoyCommissionRates
        SET DeliveryBoyId = @DeliveryBoyId,
            CommissionRatePerLitre = @CommissionRate,
            EffectiveFrom = @EffectiveFrom,
            EffectiveTo = @EffectiveTo,
            Notes = @Notes
        WHERE RateId = @RateId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 9. Stored Procedures: Unscheduled Delivery Authorizations
-- ====================================================================================

-- Stored Procedure: usp_CustomerUnscheduledDelivery_Authorize
CREATE OR ALTER PROCEDURE [dbo].[usp_CustomerUnscheduledDelivery_Authorize]
    @CustomerId INT,
    @CategoryId INT = NULL,
    @EntryDate DATE,
    @AuthorizedShift NVARCHAR(20),
    @MaxQuantity DECIMAL(18,3) = NULL,
    @Reason NVARCHAR(250),
    @AdminUserId INT,
    @AuthorizationId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = @CustomerId AND IsActive = 1)
    BEGIN
        THROW 50026, 'Customer does not exist or is inactive.', 1;
    END

    IF (@AuthorizedShift NOT IN ('Morning', 'Evening', 'Both'))
    BEGIN
        THROW 50027, 'AuthorizedShift must be ''Morning'', ''Evening'', or ''Both''.', 1;
    END

    IF (LEN(LTRIM(RTRIM(ISNULL(@Reason, '')))) < 5)
    BEGIN
        THROW 50028, 'Authorization reason must be at least 5 characters long.', 1;
    END

    IF (@CategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.MilkCategories WHERE CategoryId = @CategoryId AND IsActive = 1))
    BEGIN
        THROW 50029, 'Specified Milk Category does not exist or is inactive.', 1;
    END

    INSERT INTO dbo.CustomerUnscheduledDeliveryAuthorizations (
        CustomerId, CategoryId, EntryDate, AuthorizedShift, MaxQuantity, Reason, AuthorizedBy, AuthorizedAt, IsRevoked
    )
    VALUES (
        @CustomerId, @CategoryId, @EntryDate, @AuthorizedShift, @MaxQuantity, @Reason, @AdminUserId, SYSUTCDATETIME(), 0
    );

    SET @AuthorizationId = SCOPE_IDENTITY();
END;
GO

-- Stored Procedure: usp_CustomerUnscheduledDelivery_Revoke
CREATE OR ALTER PROCEDURE [dbo].[usp_CustomerUnscheduledDelivery_Revoke]
    @AuthorizationId BIGINT,
    @RevokeReason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.CustomerUnscheduledDeliveryAuthorizations WHERE AuthorizationId = @AuthorizationId)
    BEGIN
        THROW 50031, 'Authorization record does not exist.', 1;
    END

    IF (LEN(LTRIM(RTRIM(ISNULL(@RevokeReason, '')))) < 5)
    BEGIN
        THROW 50032, 'Revocation reason must be at least 5 characters long.', 1;
    END

    UPDATE dbo.CustomerUnscheduledDeliveryAuthorizations
    SET IsRevoked = 1,
        RevokedBy = @AdminUserId,
        RevokedAt = SYSUTCDATETIME(),
        RevokeReason = @RevokeReason
    WHERE AuthorizationId = @AuthorizationId;
END;
GO

-- ====================================================================================
-- 10. Stored Procedure: usp_DailyMilk_SaveBatch
-- Main batch runsheet save & final submission engine.
-- ====================================================================================
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

    -- 2. Validate Approved Vacation Conflict
    DECLARE @VacationConflictCustomerId INT;
    SELECT TOP 1 @VacationConflictCustomerId = h.CustomerId
    FROM @Headers h
    WHERE EXISTS (
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
                CustomerId, DeliveryBoyId, EntryDate, Shift, Status, NonDeliveryReason,
                CommissionRateSnapshot, TotalLiquidVolumeLitres, EligibleLitreQuantity, TotalAmount,
                BatchId, IsUnscheduledDelivery, AuthorizationId, AdminAuthorizedBy, CreatedAt, CreatedBy
            )
            VALUES (
                @CustId, @DeliveryBoyId, @EntryDate, @Shift, @CustStatus, @NonDelivReason,
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

-- ====================================================================================
-- 11. Stored Procedure: usp_DailyMilk_CorrectDelivery
-- Administrator post-submission delivery correction with full audit logging.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_CorrectDelivery]
    @DeliveryId BIGINT,
    @NewStatus NVARCHAR(20),
    @NonDeliveryReason NVARCHAR(250) = NULL,
    @UpdatedItems [dbo].[DailyMilkDeliveryItemInputType] READONLY,
    @CorrectionReason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (LEN(LTRIM(RTRIM(ISNULL(@CorrectionReason, '')))) < 5)
    BEGIN
        THROW 50038, 'Correction reason must be at least 5 characters long.', 1;
    END

    IF (@NewStatus NOT IN ('Completed', 'Pending', 'Not Delivered'))
    BEGIN
        THROW 50039, 'New status must be ''Completed'', ''Pending'', or ''Not Delivered''.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CustomerId INT, @EntryDate DATE, @Shift NVARCHAR(20), @PriorStatus NVARCHAR(20);
        DECLARE @PriorVolume DECIMAL(18,3), @PriorAmount DECIMAL(18,2), @BatchId BIGINT;

        SELECT
            @CustomerId = CustomerId,
            @EntryDate = EntryDate,
            @Shift = Shift,
            @PriorStatus = Status,
            @PriorVolume = TotalLiquidVolumeLitres,
            @PriorAmount = TotalAmount,
            @BatchId = BatchId
        FROM dbo.DailyMilkDeliveries WITH (UPDLOCK, HOLDLOCK)
        WHERE DeliveryId = @DeliveryId;

        IF (@CustomerId IS NULL)
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50040, 'Delivery record does not exist.', 1;
        END

        -- Check if month is already published
        IF EXISTS (
            SELECT 1
            FROM dbo.MonthlyBills
            WHERE CustomerId = @CustomerId
              AND @EntryDate BETWEEN PeriodStartDate AND PeriodEndDate
              AND IsPublished = 1
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50030, 'Cannot modify daily delivery for a published billing month. Use Bill Revision.', 1;
        END

        DECLARE @CorrectionBatchGuid UNIQUEIDENTIFIER = NEWID();

        -- Log prior items before modification
        INSERT INTO dbo.DailyMilkDeliveryCorrections (
            CorrectionBatchGuid, DeliveryId, DeliveryItemId, CustomerId, EntryDate, Shift,
            PriorStatus, NewStatus, CategoryId, PriorPackagingQuantity, NewPackagingQuantity,
            PriorLiquidVolumeLitres, NewLiquidVolumeLitres, PriorUnitPrice, NewUnitPrice,
            PriorTotalAmount, NewTotalAmount, Reason, CorrectedBy, CorrectedAt
        )
        SELECT
            @CorrectionBatchGuid,
            @DeliveryId,
            oldItem.DeliveryItemId,
            @CustomerId,
            @EntryDate,
            @Shift,
            @PriorStatus,
            @NewStatus,
            oldItem.CategoryId,
            oldItem.PackagingQuantity,
            newItem.PackagingQuantity,
            oldItem.LiquidVolumeLitres,
            ISNULL(ROUND(newItem.PackagingQuantity * cat.LitreEquivalent, 3), 0.000),
            oldItem.UnitPriceSnapshot,
            ISNULL(cp.CustomPrice, cat.DefaultPrice),
            oldItem.SubTotal,
            ISNULL(ROUND(newItem.PackagingQuantity * ISNULL(cp.CustomPrice, cat.DefaultPrice), 2), 0.00),
            @CorrectionReason,
            @AdminUserId,
            SYSUTCDATETIME()
        FROM dbo.DailyMilkDeliveryItems oldItem
        JOIN dbo.MilkCategories cat ON oldItem.CategoryId = cat.CategoryId
        LEFT JOIN @UpdatedItems newItem ON newItem.CategoryId = oldItem.CategoryId
        LEFT JOIN dbo.CustomerCustomPricing cp ON cp.CustomerId = @CustomerId
            AND cp.CategoryId = oldItem.CategoryId
            AND cp.IsActive = 1
            AND cp.EffectiveFrom <= @EntryDate
            AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @EntryDate)
        WHERE oldItem.DeliveryId = @DeliveryId;

        -- If no items existed previously (e.g. changing from Not Delivered to Completed), log header transition
        IF NOT EXISTS (SELECT 1 FROM dbo.DailyMilkDeliveryItems WHERE DeliveryId = @DeliveryId)
        BEGIN
            INSERT INTO dbo.DailyMilkDeliveryCorrections (
                CorrectionBatchGuid, DeliveryId, DeliveryItemId, CustomerId, EntryDate, Shift,
                PriorStatus, NewStatus, CategoryId, PriorPackagingQuantity, NewPackagingQuantity,
                PriorLiquidVolumeLitres, NewLiquidVolumeLitres, PriorUnitPrice, NewUnitPrice,
                PriorTotalAmount, NewTotalAmount, Reason, CorrectedBy, CorrectedAt
            )
            VALUES (
                @CorrectionBatchGuid, @DeliveryId, NULL, @CustomerId, @EntryDate, @Shift,
                @PriorStatus, @NewStatus, NULL, NULL, NULL,
                @PriorVolume, 0.000, NULL, NULL,
                @PriorAmount, 0.00, @CorrectionReason, @AdminUserId, SYSUTCDATETIME()
            );
        END

        -- Update line items in place (preserving existing DeliveryItemId and FK integrity)
        IF (@NewStatus = 'Completed')
        BEGIN
            -- 1. Update existing items in place
            UPDATE oldItem
            SET oldItem.PackagingQuantity = item.PackagingQuantity,
                oldItem.ProductTypeSnapshot = cat.ProductType,
                oldItem.UnitOfMeasureSnapshot = cat.UnitOfMeasure,
                oldItem.PricingBasisSnapshot = cat.PricingBasis,
                oldItem.BillingUnitEquivalentSnapshot = cat.BillingUnitEquivalent,
                oldItem.LitreEquivalentSnapshot = cat.LitreEquivalent,
                oldItem.LiquidVolumeLitres = ROUND(item.PackagingQuantity * cat.LitreEquivalent, 3),
                oldItem.UnitPriceSnapshot = ISNULL(cp.CustomPrice, cat.DefaultPrice),
                oldItem.SubTotal = ROUND(item.PackagingQuantity * ISNULL(cp.CustomPrice, cat.DefaultPrice), 2)
            FROM dbo.DailyMilkDeliveryItems oldItem
            JOIN @UpdatedItems item ON oldItem.DeliveryId = @DeliveryId AND oldItem.CategoryId = item.CategoryId
            JOIN dbo.MilkCategories cat ON item.CategoryId = cat.CategoryId
            LEFT JOIN dbo.CustomerCustomPricing cp ON cp.CustomerId = @CustomerId
                AND cp.CategoryId = item.CategoryId
                AND cp.IsActive = 1
                AND cp.EffectiveFrom <= @EntryDate
                AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @EntryDate);

            -- 2. Insert any new categories that didn't exist previously
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
            FROM @UpdatedItems item
            JOIN dbo.MilkCategories cat ON item.CategoryId = cat.CategoryId
            LEFT JOIN dbo.CustomerCustomPricing cp ON cp.CustomerId = @CustomerId
                AND cp.CategoryId = item.CategoryId
                AND cp.IsActive = 1
                AND cp.EffectiveFrom <= @EntryDate
                AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @EntryDate)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.DailyMilkDeliveryItems existing
                WHERE existing.DeliveryId = @DeliveryId AND existing.CategoryId = item.CategoryId
            );

            -- 3. If an existing category is not in @UpdatedItems, zero out its volume and subtotal
            UPDATE oldItem
            SET oldItem.PackagingQuantity = 0.000,
                oldItem.LiquidVolumeLitres = 0.000,
                oldItem.SubTotal = 0.00
            FROM dbo.DailyMilkDeliveryItems oldItem
            WHERE oldItem.DeliveryId = @DeliveryId
              AND NOT EXISTS (SELECT 1 FROM @UpdatedItems u WHERE u.CategoryId = oldItem.CategoryId);
        END
        ELSE
        BEGIN
            -- When status is Not Delivered or Pending, zero out quantities
            UPDATE dbo.DailyMilkDeliveryItems
            SET PackagingQuantity = 0.000,
                LiquidVolumeLitres = 0.000,
                SubTotal = 0.00
            WHERE DeliveryId = @DeliveryId;
        END

        -- Recompute delivery rollups
        DECLARE @NewVolume DECIMAL(18,3) = 0.000, @NewAmount DECIMAL(18,2) = 0.00;
        SELECT 
            @NewVolume = ISNULL(SUM(LiquidVolumeLitres), 0.000),
            @NewAmount = ISNULL(SUM(SubTotal), 0.00)
        FROM dbo.DailyMilkDeliveryItems
        WHERE DeliveryId = @DeliveryId;

        UPDATE dbo.DailyMilkDeliveries
        SET Status = @NewStatus,
            NonDeliveryReason = @NonDeliveryReason,
            TotalLiquidVolumeLitres = @NewVolume,
            EligibleLitreQuantity = CASE WHEN @NewStatus = 'Completed' THEN @NewVolume ELSE 0.000 END,
            TotalAmount = CASE WHEN @NewStatus = 'Completed' THEN @NewAmount ELSE 0.00 END,
            IsCorrected = 1,
            LastCorrectedAt = SYSUTCDATETIME(),
            LastCorrectedBy = @AdminUserId,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE DeliveryId = @DeliveryId;

        -- Recompute parent batch rollups if applicable
        IF (@BatchId IS NOT NULL)
        BEGIN
            UPDATE b
            SET b.CompletedCount = s.CompCount,
                b.PendingCount = s.PendCount,
                b.NotDeliveredCount = s.NotDelivCount,
                b.TotalLiquidVolumeLitres = s.TotVolume,
                b.TotalAmount = s.TotAmount,
                b.UpdatedAt = SYSUTCDATETIME(),
                b.UpdatedBy = @AdminUserId
            FROM dbo.DailyDeliveryBatches b
            CROSS APPLY (
                SELECT
                    SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) AS CompCount,
                    SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) AS PendCount,
                    SUM(CASE WHEN Status = 'Not Delivered' THEN 1 ELSE 0 END) AS NotDelivCount,
                    ISNULL(SUM(TotalLiquidVolumeLitres), 0.000) AS TotVolume,
                    ISNULL(SUM(TotalAmount), 0.00) AS TotAmount
                FROM dbo.DailyMilkDeliveries
                WHERE BatchId = @BatchId
            ) s
            WHERE b.BatchId = @BatchId;
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

-- ====================================================================================
-- 12. Stored Procedure: usp_DailyMilk_GetRoster
-- Retrieves delivery roster for a delivery boy or admin view.
-- ====================================================================================
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
      AND (@DeliveryBoyId IS NULL OR c.CurrentDeliveryBoyId = @DeliveryBoyId)
      AND (@RouteId IS NULL OR c.RouteId = @RouteId)
      AND (c.PreferredShift = 'Both' OR c.PreferredShift = @Shift)
    ORDER BY c.DeliverySequence ASC, c.CustomerCode ASC;
END;
GO

-- ====================================================================================
-- 13. Stored Procedure: usp_DailyMilk_GetDeliveriesContinuous
-- Implements Composite Cursor Pagination (EntryDate DESC, Shift DESC, DeliveryId DESC).
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_GetDeliveriesContinuous]
    @CustomerId INT = NULL,
    @RouteId INT = NULL,
    @DeliveryBoyId INT = NULL,
    @StartDate DATE = NULL,
    @EndDate DATE = NULL,
    @Shift NVARCHAR(20) = NULL,
    @Status NVARCHAR(20) = NULL,
    @IsUnscheduledOnly BIT = NULL,
    @CursorEntryDate DATE = NULL,
    @CursorShift NVARCHAR(20) = NULL,
    @CursorDeliveryId BIGINT = NULL,
    @PageSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CursorShiftRank INT = 0;
    IF (@CursorShift = 'Evening') SET @CursorShiftRank = 2;
    ELSE IF (@CursorShift = 'Morning') SET @CursorShiftRank = 1;

    DECLARE @FetchCount INT = @PageSize + 1;

    WITH OrderedDeliveries AS (
        SELECT
            d.DeliveryId,
            d.CustomerId,
            c.CustomerCode,
            c.FullName AS CustomerName,
            c.Area,
            c.RouteId,
            r.RouteName,
            d.DeliveryBoyId,
            u.FullName AS DeliveryBoyName,
            d.EntryDate,
            d.Shift,
            d.Status,
            d.NonDeliveryReason,
            d.CommissionRateSnapshot,
            d.TotalLiquidVolumeLitres,
            d.EligibleLitreQuantity,
            d.TotalAmount,
            d.IsUnscheduledDelivery,
            d.IsCorrected,
            d.CreatedAt,
            CASE d.Shift WHEN 'Evening' THEN 2 WHEN 'Morning' THEN 1 ELSE 0 END AS ShiftRank
        FROM dbo.DailyMilkDeliveries d
        JOIN dbo.Customers c ON d.CustomerId = c.CustomerId
        JOIN dbo.Routes r ON c.RouteId = r.RouteId
        JOIN dbo.DeliveryBoys db ON d.DeliveryBoyId = db.DeliveryBoyId
        JOIN dbo.Users u ON db.UserId = u.UserId
        WHERE (@CustomerId IS NULL OR d.CustomerId = @CustomerId)
          AND (@RouteId IS NULL OR c.RouteId = @RouteId)
          AND (@DeliveryBoyId IS NULL OR d.DeliveryBoyId = @DeliveryBoyId)
          AND (@StartDate IS NULL OR d.EntryDate >= @StartDate)
          AND (@EndDate IS NULL OR d.EntryDate <= @EndDate)
          AND (@Shift IS NULL OR d.Shift = @Shift)
          AND (@Status IS NULL OR d.Status = @Status)
          AND (@IsUnscheduledOnly IS NULL OR @IsUnscheduledOnly = 0 OR d.IsUnscheduledDelivery = 1)
          AND (
              @CursorEntryDate IS NULL
              OR d.EntryDate < @CursorEntryDate
              OR (
                  d.EntryDate = @CursorEntryDate
                  AND (
                      (CASE d.Shift WHEN 'Evening' THEN 2 WHEN 'Morning' THEN 1 ELSE 0 END) < @CursorShiftRank
                      OR (
                          (CASE d.Shift WHEN 'Evening' THEN 2 WHEN 'Morning' THEN 1 ELSE 0 END) = @CursorShiftRank
                          AND d.DeliveryId < @CursorDeliveryId
                      )
                  )
              )
          )
    )
    SELECT TOP (@FetchCount) *
    FROM OrderedDeliveries
    ORDER BY EntryDate DESC, ShiftRank DESC, DeliveryId DESC;
END;
GO

-- ====================================================================================
-- 14. Stored Procedure: usp_DailyMilk_GetSummary
-- Computes aggregated delivery, litre, monetary, and commission rollups.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DailyMilk_GetSummary]
    @EntryDate DATE,
    @Shift NVARCHAR(20),
    @DeliveryBoyId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        @EntryDate AS EntryDate,
        @Shift AS Shift,
        COUNT(DISTINCT d.CustomerId) AS TotalAssignedCustomers,
        SUM(CASE WHEN d.Status = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
        SUM(CASE WHEN d.Status = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
        SUM(CASE WHEN d.Status = 'Not Delivered' THEN 1 ELSE 0 END) AS NotDeliveredCount,
        ISNULL(SUM(CASE WHEN d.Status = 'Completed' THEN d.TotalLiquidVolumeLitres ELSE 0.000 END), 0.000) AS TotalLiquidVolumeLitres,
        ISNULL(SUM(CASE WHEN d.Status = 'Completed' THEN d.TotalAmount ELSE 0.00 END), 0.00) AS TotalAmount,
        ISNULL(SUM(CASE WHEN d.Status = 'Completed' THEN ROUND(d.EligibleLitreQuantity * d.CommissionRateSnapshot, 2) ELSE 0.00 END), 0.00) AS TotalCommissionAmount
    FROM dbo.DailyMilkDeliveries d
    WHERE d.EntryDate = @EntryDate
      AND (@Shift IS NULL OR d.Shift = @Shift)
      AND (@DeliveryBoyId IS NULL OR d.DeliveryBoyId = @DeliveryBoyId);
END;
GO

-- ====================================================================================
-- 15. Stored Procedure: usp_MonthlyBilling_GenerateCustomerBill
-- Generates monthly bill enforcing explicit approved-vacation exclusion invariant.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_GenerateCustomerBill]
    @CustomerId INT,
    @BillingMonth TINYINT,
    @BillingYear SMALLINT,
    @AdminUserId INT,
    @BillId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PeriodStartDate DATE = DATEFROMPARTS(@BillingYear, @BillingMonth, 1);
    DECLARE @PeriodEndDate DATE = EOMONTH(@PeriodStartDate);

    -- Check if month is already published
    IF EXISTS (
        SELECT 1 FROM dbo.MonthlyBills
        WHERE CustomerId = @CustomerId AND BillingMonth = @BillingMonth AND BillingYear = @BillingYear AND IsPublished = 1
    )
    BEGIN
        THROW 50030, 'Cannot regenerate an already published bill. Use Bill Revision.', 1;
    END

    -- Calculate Milk Totals enforcing explicit approved-vacation exclusion invariant
    DECLARE @CurrentMonthMilkAmount DECIMAL(18,2) = 0.00;
    DECLARE @TotalLiquidMilkVolumeLitres DECIMAL(18,3) = 0.000;

    SELECT
        @CurrentMonthMilkAmount = ISNULL(SUM(items.SubTotal), 0.00),
        @TotalLiquidMilkVolumeLitres = ISNULL(SUM(items.LiquidVolumeLitres), 0.000)
    FROM dbo.DailyMilkDeliveries dmd
    JOIN dbo.DailyMilkDeliveryItems items ON dmd.DeliveryId = items.DeliveryId
    WHERE dmd.CustomerId = @CustomerId
      AND dmd.EntryDate BETWEEN @PeriodStartDate AND @PeriodEndDate
      AND dmd.Status = 'Completed'
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.CustomerVacations v
          WHERE v.CustomerId = dmd.CustomerId
            AND v.Status = 'Approved'
            AND dmd.EntryDate BETWEEN v.StartDate AND v.EndDate
            AND (v.Shift = 'Both' OR v.Shift = dmd.Shift)
      );

    -- Previous balance snapshot
    DECLARE @PreviousBalance DECIMAL(18,2) = 0.00;
    SELECT TOP 1 @PreviousBalance = BalanceAmount
    FROM dbo.MonthlyBills
    WHERE CustomerId = @CustomerId
      AND (BillingYear < @BillingYear OR (BillingYear = @BillingYear AND BillingMonth < @BillingMonth))
    ORDER BY BillingYear DESC, BillingMonth DESC;

    SET @PreviousBalance = ISNULL(@PreviousBalance, 0.00);

    DECLARE @Adjustments DECIMAL(18,2) = 0.00;
    DECLARE @CurrentMonthInvoice DECIMAL(18,2) = @CurrentMonthMilkAmount - @Adjustments;
    DECLARE @TotalStatement DECIMAL(18,2) = @PreviousBalance + @CurrentMonthInvoice;
    DECLARE @PaidAmount DECIMAL(18,2) = 0.00;
    DECLARE @BalanceAmount DECIMAL(18,2) = @TotalStatement - @PaidAmount;

    -- Upsert draft bill
    SELECT @BillId = BillId
    FROM dbo.MonthlyBills
    WHERE CustomerId = @CustomerId AND BillingMonth = @BillingMonth AND BillingYear = @BillingYear;

    IF (@BillId IS NULL)
    BEGIN
        DECLARE @BillNumber NVARCHAR(50) = 'BILL-' + CAST(@BillingYear AS NVARCHAR) + RIGHT('0' + CAST(@BillingMonth AS NVARCHAR), 2) + '-' + CAST(@CustomerId AS NVARCHAR);

        INSERT INTO dbo.MonthlyBills (
            BillNumber, CustomerId, BillingMonth, BillingYear, PeriodStartDate, PeriodEndDate,
            TotalLiquidMilkVolumeLitres, CurrentMonthMilkAmount, Adjustments, CurrentMonthInvoiceAmount,
            PreviousBalanceSnapshot, TotalStatementAmount, PaidAmount, BalanceAmount, PaymentStatus,
            IsPublished, PublishedAt, Version, CreatedAt, CreatedBy
        )
        VALUES (
            @BillNumber, @CustomerId, @BillingMonth, @BillingYear, @PeriodStartDate, @PeriodEndDate,
            @TotalLiquidMilkVolumeLitres, @CurrentMonthMilkAmount, @Adjustments, @CurrentMonthInvoice,
            @PreviousBalance, @TotalStatement, @PaidAmount, @BalanceAmount, 'Unpaid',
            0, NULL, 1, SYSUTCDATETIME(), @AdminUserId
        );
        SET @BillId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.MonthlyBills
        SET TotalLiquidMilkVolumeLitres = @TotalLiquidMilkVolumeLitres,
            CurrentMonthMilkAmount = @CurrentMonthMilkAmount,
            Adjustments = @Adjustments,
            CurrentMonthInvoiceAmount = @CurrentMonthInvoice,
            PreviousBalanceSnapshot = @PreviousBalance,
            TotalStatementAmount = @TotalStatement,
            BalanceAmount = @BalanceAmount,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE BillId = @BillId;
    END
END;
GO

-- ====================================================================================
-- 16. Security Hardening: Least-Privilege Grants for [db_dms_app]
-- ====================================================================================
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'db_dms_app' AND type = 'R')
BEGIN
    PRINT 'Configuring least-privilege permissions for role db_dms_app...';

    -- Explicitly revoke direct table write permissions on business-critical delivery entities
    -- to prevent bypass of stored-procedure validation workflows.
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[DailyDeliveryBatches] FROM [db_dms_app];
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[DailyMilkDeliveries] FROM [db_dms_app];
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[DailyMilkDeliveryItems] FROM [db_dms_app];
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[CustomerUnscheduledDeliveryAuthorizations] FROM [db_dms_app];
    REVOKE UPDATE, DELETE ON [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] FROM [db_dms_app];
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[DailyMilkDeliveryCorrections] FROM [db_dms_app];
    REVOKE INSERT, UPDATE, DELETE ON [dbo].[DeliveryBoyCommissionRates] FROM [db_dms_app];

    -- Revoke broad schema-level SELECT to enforce least-privilege table-level security
    REVOKE SELECT ON SCHEMA::dbo FROM [db_dms_app];

    -- Explicit table-level SELECT grants strictly required by DailyMilkRepository queries
    GRANT SELECT ON [dbo].[DailyDeliveryBatches] TO [db_dms_app];
    GRANT SELECT ON [dbo].[DailyMilkDeliveries] TO [db_dms_app];
    GRANT SELECT ON [dbo].[DailyMilkDeliveryItems] TO [db_dms_app];
    GRANT SELECT ON [dbo].[MilkCategories] TO [db_dms_app];
    GRANT SELECT ON [dbo].[Customers] TO [db_dms_app];
    GRANT SELECT ON [dbo].[CustomerCustomPricing] TO [db_dms_app];
    GRANT SELECT ON [dbo].[CustomerVacations] TO [db_dms_app];
    GRANT SELECT ON [dbo].[CustomerUnscheduledDeliveryAuthorizations] TO [db_dms_app];
    GRANT SELECT ON [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] TO [db_dms_app];
    GRANT SELECT ON [dbo].[DeliveryBoyCommissionRates] TO [db_dms_app];
    GRANT SELECT ON [dbo].[DeliveryBoys] TO [db_dms_app];
    GRANT SELECT ON [dbo].[DailyMilkDeliveryCorrections] TO [db_dms_app];
    GRANT SELECT ON [dbo].[MonthlyBills] TO [db_dms_app];
    GRANT SELECT ON OBJECT::dbo.CustomerSubscriptions TO [db_dms_app];
    GRANT SELECT ON OBJECT::dbo.CustomerSubscriptionDeliveryDays TO [db_dms_app];

    -- Column-level SELECT on Users: non-sensitive identity fields required for audit name lookups
    -- Actively denies PasswordHash, Salt, and full record exposure.
    GRANT SELECT (UserId, FullName) ON [dbo].[Users] TO [db_dms_app];

    -- Dedicated append-only table access where required by repository contracts
    GRANT INSERT ON [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] TO [db_dms_app];

    -- User-Defined Table Types: EXECUTE and REFERENCES
    GRANT EXECUTE, REFERENCES ON TYPE::[dbo].[DailyMilkDeliveryItemInputType] TO [db_dms_app];
    GRANT EXECUTE, REFERENCES ON TYPE::[dbo].[DailyMilkDeliveryHeaderInputType] TO [db_dms_app];

    -- Stored Procedures
    GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_CreateCommissionRate] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_UpdateCommissionRate] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_CustomerUnscheduledDelivery_Authorize] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_CustomerUnscheduledDelivery_Revoke] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DailyMilk_SaveBatch] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DailyMilk_CorrectDelivery] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DailyMilk_GetRoster] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DailyMilk_GetDeliveriesContinuous] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_DailyMilk_GetSummary] TO [db_dms_app];
    GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_GenerateCustomerBill] TO [db_dms_app];
END
GO

PRINT '====================================================================================';
PRINT '07_DailyMilkManagement migration completed successfully!';
PRINT '====================================================================================';
GO
