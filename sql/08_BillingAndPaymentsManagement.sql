-- ====================================================================================
-- Dairy Management System — Day 5 Database Engineering
-- Script 08: Billing & Payments Management Schema, Ledger, Procedures & Security
-- Engineer: Kevin Gajera (Backend / SQL Server Developer)
-- Architect: Rachit Parsana (Lead Project Architect)
-- Date: 19-Nov-2026
-- Description: Production-grade, idempotent database implementation for Day 5 Billing
--              and Payments. Implements the authoritative append-only allocation event ledger,
--              explicit relational legacy migration with a 5-point automated reconciliation gate,
--              deterministic LIFO multi-payment bill revision finalization, two-tiered payment
--              void idempotency, snapshotted due dates, and static SQL ownership chaining security.
-- ====================================================================================

USE [DairyManagementDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting 08_BillingAndPaymentsManagement migration...';
PRINT '====================================================================================';
GO

-- ====================================================================================
-- 1. Schema Additions on Existing Tables
-- ====================================================================================

-- 1.1 Add PaymentDueDate to dbo.MonthlyBills if not already present
IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[MonthlyBills]') 
      AND name = N'PaymentDueDate'
)
BEGIN
    PRINT 'Adding column PaymentDueDate DATE NULL to dbo.MonthlyBills...';
    ALTER TABLE [dbo].[MonthlyBills]
    ADD [PaymentDueDate] DATE NULL;
    PRINT 'Column PaymentDueDate added successfully.';
END
ELSE
BEGIN
    PRINT 'Column PaymentDueDate already exists on dbo.MonthlyBills.';
END
GO

-- ====================================================================================
-- 2. New Table: dbo.PaymentAllocationEvents (Authoritative Append-Only Ledger)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PaymentAllocationEvents')
BEGIN
    PRINT 'Creating table dbo.PaymentAllocationEvents...';
    CREATE TABLE [dbo].[PaymentAllocationEvents] (
        [AllocationEventId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_PaymentAllocationEvents] PRIMARY KEY CLUSTERED,
        [PaymentId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocationEvents_PaymentId] REFERENCES [dbo].[Payments]([PaymentId]),
        [BillId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocationEvents_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]),
        [EventType] NVARCHAR(30) NOT NULL CONSTRAINT [CK_PaymentAllocationEvents_EventType] CHECK ([EventType] IN (
            'InitialAllocation',
            'ReversalAdjustment',
            'VoidReversal',
            'AdvanceCreditAllocation'
        )),
        [OriginalAllocationEventId] BIGINT NULL CONSTRAINT [FK_PaymentAllocationEvents_OriginalEvent] REFERENCES [dbo].[PaymentAllocationEvents]([AllocationEventId]),
        [Amount] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_PaymentAllocationEvents_Amount] CHECK ([Amount] > 0),
        [Direction] SMALLINT NOT NULL CONSTRAINT [CK_PaymentAllocationEvents_Direction] CHECK ([Direction] IN (1, -1)),
        [SignedAmount] AS ([Amount] * [Direction]) PERSISTED,
        [Reason] NVARCHAR(500) NOT NULL,
        [RelatedRevisionId] BIGINT NULL CONSTRAINT [FK_PaymentAllocationEvents_RevisionId] REFERENCES [dbo].[MonthlyBillRevisions]([RevisionId]),
        [LegacyAllocationId] BIGINT NULL,  -- Populated strictly for migrated legacy rows; NULL for runtime events
        [PerformedBy] INT NOT NULL CONSTRAINT [FK_PaymentAllocationEvents_PerformedBy] REFERENCES [dbo].[Users]([UserId]),
        [PerformedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAllocationEvents_PerformedAt] DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Table dbo.PaymentAllocationEvents created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.PaymentAllocationEvents already exists.';
END
GO

-- 2.1 Unique Filtered Index enforcing 1:1 legacy migration uniqueness
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = N'UQ_PaymentAllocationEvents_LegacyAllocationId' 
      AND object_id = OBJECT_ID(N'[dbo].[PaymentAllocationEvents]')
)
BEGIN
    PRINT 'Creating unique filtered index UQ_PaymentAllocationEvents_LegacyAllocationId...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_LegacyAllocationId]
    ON [dbo].[PaymentAllocationEvents]([LegacyAllocationId])
    WHERE [LegacyAllocationId] IS NOT NULL;
    PRINT 'Index UQ_PaymentAllocationEvents_LegacyAllocationId created.';
END
GO

-- 2.2 Secondary Filtered Unique Index enforcing 1:1 void reversal uniqueness per original event
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = N'UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent' 
      AND object_id = OBJECT_ID(N'[dbo].[PaymentAllocationEvents]')
)
BEGIN
    PRINT 'Creating unique filtered index UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent]
    ON [dbo].[PaymentAllocationEvents]([OriginalAllocationEventId])
    WHERE [EventType] = 'VoidReversal' AND [OriginalAllocationEventId] IS NOT NULL;
    PRINT 'Index UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent created.';
END
GO

-- 2.3 Performance indexes for event queries and LIFO aggregation
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = N'IX_PaymentAllocationEvents_BillId_Direction' 
      AND object_id = OBJECT_ID(N'[dbo].[PaymentAllocationEvents]')
)
BEGIN
    PRINT 'Creating performance index IX_PaymentAllocationEvents_BillId_Direction...';
    CREATE NONCLUSTERED INDEX [IX_PaymentAllocationEvents_BillId_Direction]
    ON [dbo].[PaymentAllocationEvents]([BillId], [Direction])
    INCLUDE ([Amount], [SignedAmount], [OriginalAllocationEventId], [PaymentId], [EventType], [PerformedAt]);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = N'IX_PaymentAllocationEvents_PaymentId_Direction' 
      AND object_id = OBJECT_ID(N'[dbo].[PaymentAllocationEvents]')
)
BEGIN
    PRINT 'Creating performance index IX_PaymentAllocationEvents_PaymentId_Direction...';
    CREATE NONCLUSTERED INDEX [IX_PaymentAllocationEvents_PaymentId_Direction]
    ON [dbo].[PaymentAllocationEvents]([PaymentId], [Direction])
    INCLUDE ([Amount], [SignedAmount], [OriginalAllocationEventId], [BillId], [EventType], [PerformedAt]);
END
GO

-- ====================================================================================
-- 3. New Table: dbo.PaymentAuditLogs (Immutable Payment Audit History)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PaymentAuditLogs')
BEGIN
    PRINT 'Creating table dbo.PaymentAuditLogs...';
    CREATE TABLE [dbo].[PaymentAuditLogs] (
        [AuditLogId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_PaymentAuditLogs] PRIMARY KEY CLUSTERED,
        [PaymentId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAuditLogs_PaymentId] REFERENCES [dbo].[Payments]([PaymentId]),
        [Action] NVARCHAR(50) NOT NULL,
        [OldAmount] DECIMAL(18,2) NULL,
        [NewAmount] DECIMAL(18,2) NULL,
        [OldStatus] NVARCHAR(50) NULL,
        [NewStatus] NVARCHAR(50) NULL,
        [Reason] NVARCHAR(500) NULL,
        [PerformedBy] INT NOT NULL CONSTRAINT [FK_PaymentAuditLogs_PerformedBy] REFERENCES [dbo].[Users]([UserId]),
        [PerformedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAuditLogs_PerformedAt] DEFAULT (SYSUTCDATETIME())
    );
    PRINT 'Table dbo.PaymentAuditLogs created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.PaymentAuditLogs already exists.';
END
GO

-- ====================================================================================
-- 4. Financial Immutability Triggers
-- ====================================================================================

-- 4.1 Prevent direct DELETE on published bills
CREATE OR ALTER TRIGGER [dbo].[trg_MonthlyBills_PreventDirectDelete]
ON [dbo].[MonthlyBills]
FOR DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE [IsPublished] = 1)
    BEGIN
        THROW 50050, 'Direct deletion of published bills is strictly prohibited. Bills must be corrected via formal revisions.', 1;
    END;
END;
GO

-- 4.2 Prevent UPDATE or DELETE on revision audit history
CREATE OR ALTER TRIGGER [dbo].[trg_MonthlyBillRevisions_PreventModification]
ON [dbo].[MonthlyBillRevisions]
FOR UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50051, 'dbo.MonthlyBillRevisions is an immutable audit log. UPDATE and DELETE operations are strictly prohibited.', 1;
END;
GO

-- 4.3 Prevent direct physical DELETE on payment records
CREATE OR ALTER TRIGGER [dbo].[trg_Payments_PreventDirectDelete]
ON [dbo].[Payments]
FOR DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50052, 'Physical deletion of payment receipts is strictly prohibited. Use dbo.usp_Payment_Void to void payments.', 1;
END;
GO

-- 4.4 Prevent UPDATE or DELETE on allocation event ledger
CREATE OR ALTER TRIGGER [dbo].[trg_PaymentAllocationEvents_PreventModification]
ON [dbo].[PaymentAllocationEvents]
FOR UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50053, 'dbo.PaymentAllocationEvents is an append-only ledger. UPDATE and DELETE operations are strictly prohibited.', 1;
END;
GO

-- 4.5 Prevent UPDATE or DELETE on payment audit logs
CREATE OR ALTER TRIGGER [dbo].[trg_PaymentAuditLogs_PreventModification]
ON [dbo].[PaymentAuditLogs]
FOR UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50054, 'dbo.PaymentAuditLogs is an immutable audit trail. UPDATE and DELETE operations are strictly prohibited.', 1;
END;
GO

-- 4.6 Legacy Table Archival Trigger: Make dbo.PaymentAllocations permanently read-only
CREATE OR ALTER TRIGGER [dbo].[trg_PaymentAllocations_LegacyReadOnly]
ON [dbo].[PaymentAllocations]
FOR INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50055, 'dbo.PaymentAllocations is archived and read-only. All payment allocations must be written as append-only events to dbo.PaymentAllocationEvents via approved stored procedures.', 1;
END;
GO

-- ====================================================================================
-- 5. Transactional Migration of Legacy Allocations with 5-Point Reconciliation Gate
-- ====================================================================================
PRINT 'Executing transactional migration of legacy PaymentAllocations...';

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Step A: Migrate Active Legacy Allocations (IsVoided = 0)
INSERT INTO [dbo].[PaymentAllocationEvents] (
    [PaymentId],
    [BillId],
    [EventType],
    [OriginalAllocationEventId],
    [Amount],
    [Direction],
    [Reason],
    [RelatedRevisionId],
    [LegacyAllocationId],
    [PerformedBy],
    [PerformedAt]
)
SELECT 
    pa.[PaymentId],
    pa.[BillId],
    'InitialAllocation',
    NULL,
    pa.[AllocatedAmount],
    1,
    'Migrated legacy active allocation',
    NULL,
    pa.[AllocationId],
    pa.[AllocatedBy],
    pa.[AllocatedAt]
FROM [dbo].[PaymentAllocations] pa
WHERE pa.[IsVoided] = 0
  AND NOT EXISTS (
      SELECT 1 
      FROM [dbo].[PaymentAllocationEvents] pae 
      WHERE pae.[LegacyAllocationId] = pa.[AllocationId]
  );

-- Step B: Migrate Historical Voided Legacy Allocations (IsVoided = 1)
DECLARE @LegacyVoidCursor CURSOR;
DECLARE @LegAllocId BIGINT, @LegPayId BIGINT, @LegBillId BIGINT, @LegAmt DECIMAL(18,2), @LegUser INT, @LegTime DATETIME2(7);
DECLARE @NewInitialEventId BIGINT;

SET @LegacyVoidCursor = CURSOR LOCAL FAST_FORWARD FOR
    SELECT pa.[AllocationId], pa.[PaymentId], pa.[BillId], pa.[AllocatedAmount], pa.[AllocatedBy], pa.[AllocatedAt]
    FROM [dbo].[PaymentAllocations] pa
    WHERE pa.[IsVoided] = 1
      AND NOT EXISTS (
          SELECT 1 
          FROM [dbo].[PaymentAllocationEvents] pae 
          WHERE pae.[LegacyAllocationId] = pa.[AllocationId]
      );

OPEN @LegacyVoidCursor;
FETCH NEXT FROM @LegacyVoidCursor INTO @LegAllocId, @LegPayId, @LegBillId, @LegAmt, @LegUser, @LegTime;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- 1. Insert Initial Event (carrying LegacyAllocationId)
    INSERT INTO [dbo].[PaymentAllocationEvents] (
        [PaymentId], [BillId], [EventType], [OriginalAllocationEventId],
        [Amount], [Direction], [Reason], [RelatedRevisionId],
        [LegacyAllocationId], [PerformedBy], [PerformedAt]
    ) VALUES (
        @LegPayId, @LegBillId, 'InitialAllocation', NULL,
        @LegAmt, 1, 'Migrated historical voided allocation (Initial)', NULL,
        @LegAllocId, @LegUser, @LegTime
    );

    SET @NewInitialEventId = SCOPE_IDENTITY();

    -- 2. Insert Linked VoidReversal Event (guarded by UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent)
    INSERT INTO [dbo].[PaymentAllocationEvents] (
        [PaymentId], [BillId], [EventType], [OriginalAllocationEventId],
        [Amount], [Direction], [Reason], [RelatedRevisionId],
        [LegacyAllocationId], [PerformedBy], [PerformedAt]
    ) VALUES (
        @LegPayId, @LegBillId, 'VoidReversal', @NewInitialEventId,
        @LegAmt, -1, 'Migrated historical voided allocation (Reversal)', NULL,
        NULL, @LegUser, @LegTime
    );

    FETCH NEXT FROM @LegacyVoidCursor INTO @LegAllocId, @LegPayId, @LegBillId, @LegAmt, @LegUser, @LegTime;
END;
CLOSE @LegacyVoidCursor;
DEALLOCATE @LegacyVoidCursor;

-- ====================================================================================
-- Automated Migration-Time Reconciliation Gate (5 Mandatory Checks)
-- ====================================================================================

-- Check 1: Active Legacy Grand Total Match
DECLARE @LegacyActiveTotal DECIMAL(18,2) = (SELECT ISNULL(SUM(AllocatedAmount), 0.00) FROM [dbo].[PaymentAllocations] WHERE IsVoided = 0);
DECLARE @MigratedActiveTotal DECIMAL(18,2) = (
    SELECT ISNULL(SUM(pae.SignedAmount), 0.00)
    FROM [dbo].[PaymentAllocationEvents] pae
    JOIN [dbo].[PaymentAllocations] pa ON pae.LegacyAllocationId = pa.AllocationId
    WHERE pa.IsVoided = 0 AND pae.Direction = 1
);
IF @LegacyActiveTotal <> @MigratedActiveTotal
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50081, 'Migration Gate Check 1 Failed: Active legacy grand total does not match migrated event total.', 1;
END;

-- Check 1B: Active Row Attribute Invariance
IF EXISTS (
    SELECT 1
    FROM [dbo].[PaymentAllocations] pa
    JOIN [dbo].[PaymentAllocationEvents] pae ON pa.AllocationId = pae.LegacyAllocationId
    WHERE pa.IsVoided = 0
      AND (pae.PaymentId <> pa.PaymentId 
        OR pae.BillId <> pa.BillId 
        OR pae.Amount <> pa.AllocatedAmount 
        OR pae.Direction <> 1 
        OR pae.EventType <> 'InitialAllocation')
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50082, 'Migration Gate Check 1B Failed: Active row attribute mismatch detected.', 1;
END;

-- Check 2: Voided Allocations Event Pair & Net Zero Reconciliation
DECLARE @VoidedDiscrepancies INT;
SELECT @VoidedDiscrepancies = COUNT(*)
FROM (
    SELECT pa.AllocationId
    FROM [dbo].[PaymentAllocations] pa
    LEFT JOIN [dbo].[PaymentAllocationEvents] init_evt 
           ON pa.AllocationId = init_evt.LegacyAllocationId
    LEFT JOIN [dbo].[PaymentAllocationEvents] rev_evt 
           ON init_evt.AllocationEventId = rev_evt.OriginalAllocationEventId 
          AND rev_evt.EventType = 'VoidReversal'
    WHERE pa.IsVoided = 1
    GROUP BY pa.AllocationId
    HAVING COUNT(init_evt.AllocationEventId) <> 1
        OR COUNT(rev_evt.AllocationEventId) <> 1
        OR ISNULL(SUM(init_evt.SignedAmount + rev_evt.SignedAmount), 0.00) <> 0.00
) disc;

IF @VoidedDiscrepancies > 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50083, 'Migration Gate Check 2 Failed: Voided allocations net zero check failed.', 1;
END;

-- Check 3: Payment-Level Reconciliation
IF EXISTS (
    SELECT 1
    FROM (
        SELECT PaymentId, SUM(AllocatedAmount) AS LegacyTotal
        FROM [dbo].[PaymentAllocations]
        WHERE IsVoided = 0
        GROUP BY PaymentId
    ) leg
    FULL OUTER JOIN (
        SELECT e.PaymentId, SUM(e.SignedAmount) AS MigratedTotal
        FROM [dbo].[PaymentAllocationEvents] e
        WHERE e.LegacyAllocationId IS NOT NULL
           OR e.OriginalAllocationEventId IN (
               SELECT AllocationEventId 
               FROM [dbo].[PaymentAllocationEvents] 
               WHERE LegacyAllocationId IS NOT NULL
           )
        GROUP BY e.PaymentId
    ) mig ON leg.PaymentId = mig.PaymentId
    WHERE ISNULL(leg.LegacyTotal, 0.00) <> ISNULL(mig.MigratedTotal, 0.00)
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50084, 'Migration Gate Check 3 Failed: Payment-level discrepancy detected.', 1;
END;

-- Check 4: Bill-Level Reconciliation
IF EXISTS (
    SELECT 1
    FROM (
        SELECT BillId, SUM(AllocatedAmount) AS LegacyTotal
        FROM [dbo].[PaymentAllocations]
        WHERE IsVoided = 0
        GROUP BY BillId
    ) leg
    FULL OUTER JOIN (
        SELECT e.BillId, SUM(e.SignedAmount) AS MigratedTotal
        FROM [dbo].[PaymentAllocationEvents] e
        WHERE e.LegacyAllocationId IS NOT NULL
           OR e.OriginalAllocationEventId IN (
               SELECT AllocationEventId 
               FROM [dbo].[PaymentAllocationEvents] 
               WHERE LegacyAllocationId IS NOT NULL
           )
        GROUP BY e.BillId
    ) mig ON leg.BillId = mig.BillId
    WHERE ISNULL(leg.LegacyTotal, 0.00) <> ISNULL(mig.MigratedTotal, 0.00)
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50085, 'Migration Gate Check 4 Failed: Bill-level discrepancy detected.', 1;
END;

-- Check 5: Identifier & Relational Integrity
IF EXISTS (
    SELECT 1 FROM [dbo].[PaymentAllocations] pa
    WHERE pa.IsVoided = 0
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[PaymentAllocationEvents] pae 
          WHERE pae.LegacyAllocationId = pa.AllocationId
      )
) OR EXISTS (
    SELECT LegacyAllocationId
    FROM [dbo].[PaymentAllocationEvents]
    WHERE LegacyAllocationId IS NOT NULL
    GROUP BY LegacyAllocationId
    HAVING COUNT(*) > 1
) OR EXISTS (
    SELECT rev.AllocationEventId
    FROM [dbo].[PaymentAllocationEvents] rev
    LEFT JOIN [dbo].[PaymentAllocationEvents] init ON rev.OriginalAllocationEventId = init.AllocationEventId
    WHERE rev.EventType = 'VoidReversal'
      AND (init.AllocationEventId IS NULL 
        OR rev.PaymentId <> init.PaymentId 
        OR rev.BillId <> init.BillId)
)
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50086, 'Migration Gate Check 5 Failed: Relational integrity check failed.', 1;
END;

COMMIT TRANSACTION;
PRINT 'Transactional migration completed successfully. All 5 reconciliation checks PASSED.';
GO

-- ====================================================================================
-- 6. Decommission Legacy Multi-Bill Stored Procedure
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_AllocatePayment]
    @PaymentId BIGINT,
    @AdminUserId INT,
    @Allocations [dbo].[PaymentAllocationItemType] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50060, 'Legacy procedure dbo.usp_AllocatePayment has been decommissioned. Multi-bill allocation via TVP is obsolete. Use dbo.usp_Payment_RecordAndAllocateSingle or dbo.usp_Payment_AllocateAdvanceCreditSingle.', 1;
END;
GO

-- ====================================================================================
-- 7. Stored Procedures Implementation (Compiled Static SQL)
-- ====================================================================================

-- ------------------------------------------------------------------------------------
-- Procedure 1: dbo.usp_MonthlyBilling_GenerateCustomerBill
-- Generates or recalculates a calendar-month draft bill for a single customer.
-- ------------------------------------------------------------------------------------
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

    -- 1. Validation
    IF @BillingMonth NOT BETWEEN 1 AND 12
        THROW 50001, 'Invalid billing month. Must be between 1 and 12.', 1;

    IF @BillingYear NOT BETWEEN 2000 AND 2100
        THROW 50002, 'Invalid billing year.', 1;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Customers] WHERE [CustomerId] = @CustomerId)
        THROW 50003, 'Customer does not exist.', 1;

    DECLARE @PeriodStartDate DATE = DATEFROMPARTS(@BillingYear, @BillingMonth, 1);
    DECLARE @PeriodEndDate DATE = EOMONTH(@PeriodStartDate);

    -- 2. Verify published bill does not exist for this period
    DECLARE @ExistingBillId BIGINT, @IsPublished BIT, @ExistingVersion INT, @ExistingAdjustments DECIMAL(18,2);
    SELECT 
        @ExistingBillId = [BillId],
        @IsPublished = [IsPublished],
        @ExistingVersion = [Version],
        @ExistingAdjustments = [Adjustments]
    FROM [dbo].[MonthlyBills]
    WHERE [CustomerId] = @CustomerId
      AND [BillingMonth] = @BillingMonth
      AND [BillingYear] = @BillingYear;

    IF @ExistingBillId IS NOT NULL AND @IsPublished = 1
    BEGIN
        THROW 50010, 'A published bill already exists for this customer and billing period. Modifications require a formal bill revision.', 1;
    END;

    -- 3. Calculate delivery quantities and milk amounts (Completed deliveries only, excluding approved vacations)
    -- Grouped by Milk Category
    DECLARE @ItemSummary TABLE (
        [CategoryId] INT NOT NULL,
        [ProductTypeSnapshot] NVARCHAR(20) NOT NULL,
        [UnitOfMeasureSnapshot] NVARCHAR(20) NOT NULL,
        [PricingBasisSnapshot] NVARCHAR(20) NOT NULL,
        [BillingUnitEquivalentSnapshot] DECIMAL(18,3) NOT NULL,
        [LitreEquivalentSnapshot] DECIMAL(18,3) NOT NULL,
        [UnitPriceSnapshot] DECIMAL(18,2) NOT NULL,
        [DeliveredDaysCount] INT NOT NULL,
        [TotalPackagingQuantity] DECIMAL(18,3) NOT NULL,
        [TotalLiquidVolumeLitres] DECIMAL(18,3) NOT NULL,
        [SubTotal] DECIMAL(18,2) NOT NULL
    );

    INSERT INTO @ItemSummary (
        [CategoryId],
        [ProductTypeSnapshot],
        [UnitOfMeasureSnapshot],
        [PricingBasisSnapshot],
        [BillingUnitEquivalentSnapshot],
        [LitreEquivalentSnapshot],
        [UnitPriceSnapshot],
        [DeliveredDaysCount],
        [TotalPackagingQuantity],
        [TotalLiquidVolumeLitres],
        [SubTotal]
    )
    SELECT 
        dmi.[CategoryId],
        dmi.[ProductTypeSnapshot],
        dmi.[UnitOfMeasureSnapshot],
        dmi.[PricingBasisSnapshot],
        dmi.[BillingUnitEquivalentSnapshot],
        dmi.[LitreEquivalentSnapshot],
        dmi.[UnitPriceSnapshot],
        COUNT(DISTINCT d.[EntryDate]) AS DeliveredDaysCount,
        SUM(dmi.[PackagingQuantity]) AS TotalPackagingQuantity,
        SUM(dmi.[LiquidVolumeLitres]) AS TotalLiquidVolumeLitres,
        SUM(dmi.[SubTotal]) AS SubTotal
    FROM [dbo].[DailyMilkDeliveries] d
    JOIN [dbo].[DailyMilkDeliveryItems] dmi ON d.[DeliveryId] = dmi.[DeliveryId]
    WHERE d.[CustomerId] = @CustomerId
      AND d.[EntryDate] >= @PeriodStartDate
      AND d.[EntryDate] <= @PeriodEndDate
      AND d.[Status] = 'Completed'
      AND NOT EXISTS (
          -- Approved vacation exclusion guard
          SELECT 1 
          FROM [dbo].[CustomerVacations] cv
          WHERE cv.[CustomerId] = @CustomerId
            AND cv.[Status] = 'Approved'
            AND d.[EntryDate] >= cv.[StartDate]
            AND d.[EntryDate] <= cv.[EndDate]
            AND (cv.[Shift] = 'Both' OR cv.[Shift] = d.[Shift])
      )
    GROUP BY 
        dmi.[CategoryId],
        dmi.[ProductTypeSnapshot],
        dmi.[UnitOfMeasureSnapshot],
        dmi.[PricingBasisSnapshot],
        dmi.[BillingUnitEquivalentSnapshot],
        dmi.[LitreEquivalentSnapshot],
        dmi.[UnitPriceSnapshot];

    -- 4. Calculate Aggregate Totals
    DECLARE @TotalLiquidVolume DECIMAL(18,3) = ISNULL((SELECT SUM(TotalLiquidVolumeLitres) FROM @ItemSummary), 0.000);
    DECLARE @CurrentMonthMilkAmount DECIMAL(18,2) = ISNULL((SELECT SUM(SubTotal) FROM @ItemSummary), 0.00);
    DECLARE @Adjustments DECIMAL(18,2) = ISNULL(@ExistingAdjustments, 0.00);
    DECLARE @CurrentMonthInvoiceAmount DECIMAL(18,2) = @CurrentMonthMilkAmount - @Adjustments;

    -- Previous Balance Snapshot: Unpaid balance of all prior PUBLISHED bills (informational snapshot only!)
    DECLARE @PreviousBalanceSnapshot DECIMAL(18,2) = ISNULL((
        SELECT SUM([BalanceAmount])
        FROM [dbo].[MonthlyBills]
        WHERE [CustomerId] = @CustomerId
          AND [IsPublished] = 1
          AND [PeriodEndDate] < @PeriodStartDate
    ), 0.00);

    DECLARE @TotalStatementAmount DECIMAL(18,2) = @PreviousBalanceSnapshot + @CurrentMonthInvoiceAmount;
    DECLARE @PaidAmount DECIMAL(18,2) = 0.00;
    DECLARE @BalanceAmount DECIMAL(18,2) = @CurrentMonthInvoiceAmount - @PaidAmount;

    -- Deterministic Bill Number: BILL-YYYYMM-CustomerID
    DECLARE @BillNumber NVARCHAR(30) = N'BILL-' + CAST(@BillingYear AS NVARCHAR(4)) + RIGHT(N'0' + CAST(@BillingMonth AS NVARCHAR(2)), 2) + N'-' + CAST(@CustomerId AS NVARCHAR(10));

    BEGIN TRANSACTION;

    IF @ExistingBillId IS NOT NULL
    BEGIN
        -- Update existing draft bill
        UPDATE [dbo].[MonthlyBills]
        SET [TotalLiquidMilkVolumeLitres] = @TotalLiquidVolume,
            [CurrentMonthMilkAmount] = @CurrentMonthMilkAmount,
            [Adjustments] = @Adjustments,
            [CurrentMonthInvoiceAmount] = @CurrentMonthInvoiceAmount,
            [PreviousBalanceSnapshot] = @PreviousBalanceSnapshot,
            [TotalStatementAmount] = @TotalStatementAmount,
            [PaidAmount] = @PaidAmount,
            [BalanceAmount] = @BalanceAmount,
            [PaymentStatus] = 'Unpaid',
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [BillId] = @ExistingBillId;

        SET @BillId = @ExistingBillId;

        -- Remove existing items for clean regeneration
        DELETE FROM [dbo].[MonthlyBillItems] WHERE [BillId] = @BillId;
    END
    ELSE
    BEGIN
        -- Insert new draft bill
        INSERT INTO [dbo].[MonthlyBills] (
            [BillNumber],
            [CustomerId],
            [BillingMonth],
            [BillingYear],
            [PeriodStartDate],
            [PeriodEndDate],
            [TotalLiquidMilkVolumeLitres],
            [CurrentMonthMilkAmount],
            [Adjustments],
            [CurrentMonthInvoiceAmount],
            [PreviousBalanceSnapshot],
            [TotalStatementAmount],
            [PaidAmount],
            [BalanceAmount],
            [PaymentStatus],
            [IsPublished],
            [PublishedAt],
            [PaymentDueDate],
            [Version],
            [RevisionReason],
            [CreatedBy],
            [CreatedAt]
        ) VALUES (
            @BillNumber,
            @CustomerId,
            @BillingMonth,
            @BillingYear,
            @PeriodStartDate,
            @PeriodEndDate,
            @TotalLiquidVolume,
            @CurrentMonthMilkAmount,
            @Adjustments,
            @CurrentMonthInvoiceAmount,
            @PreviousBalanceSnapshot,
            @TotalStatementAmount,
            @PaidAmount,
            @BalanceAmount,
            'Unpaid',
            0,
            NULL,
            NULL,
            1,
            NULL,
            @AdminUserId,
            SYSUTCDATETIME()
        );

        SET @BillId = SCOPE_IDENTITY();
    END;

    -- 5. Insert Itemized Lines into MonthlyBillItems
    INSERT INTO [dbo].[MonthlyBillItems] (
        [BillId],
        [CategoryId],
        [ProductTypeSnapshot],
        [UnitOfMeasureSnapshot],
        [PricingBasisSnapshot],
        [BillingUnitEquivalentSnapshot],
        [LitreEquivalentSnapshot],
        [UnitPriceSnapshot],
        [DeliveredDaysCount],
        [TotalPackagingQuantity],
        [TotalLiquidVolumeLitres],
        [SubTotal]
    )
    SELECT 
        @BillId,
        [CategoryId],
        [ProductTypeSnapshot],
        [UnitOfMeasureSnapshot],
        [PricingBasisSnapshot],
        [BillingUnitEquivalentSnapshot],
        [LitreEquivalentSnapshot],
        [UnitPriceSnapshot],
        [DeliveredDaysCount],
        [TotalPackagingQuantity],
        [TotalLiquidVolumeLitres],
        [SubTotal]
    FROM @ItemSummary;

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 2: dbo.usp_MonthlyBilling_GenerateBulk
-- Executes bulk draft invoice generation with application lock protection.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_GenerateBulk]
    @BillingMonth TINYINT,
    @BillingYear SMALLINT,
    @RouteId INT NULL,
    @AdminUserId INT,
    @TotalEligible INT OUTPUT,
    @TotalSucceeded INT OUTPUT,
    @TotalSkipped INT OUTPUT,
    @TotalFailed INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    SET @TotalEligible = 0;
    SET @TotalSucceeded = 0;
    SET @TotalSkipped = 0;
    SET @TotalFailed = 0;

    -- 1. Acquire DMS Bulk Billing Application Lock
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock 
        @Resource = 'DMS_BulkBilling_Lock', 
        @LockMode = 'Exclusive', 
        @LockOwner = 'Session', 
        @LockTimeout = 5000;

    IF @LockResult < 0
    BEGIN
        THROW 50020, 'Another bulk billing operation is currently in progress. Please wait.', 1;
    END;

    BEGIN TRY
        DECLARE @PeriodStartDate DATE = DATEFROMPARTS(@BillingYear, @BillingMonth, 1);
        DECLARE @PeriodEndDate DATE = EOMONTH(@PeriodStartDate);

        -- Cursor over active customers (filtered by route if provided)
        DECLARE @CustomerCursor CURSOR;
        DECLARE @CurrCustomerId INT;

        SET @CustomerCursor = CURSOR LOCAL FAST_FORWARD FOR
            SELECT c.[CustomerId]
            FROM [dbo].[Customers] c
            WHERE c.[IsActive] = 1
              AND (@RouteId IS NULL OR c.[RouteId] = @RouteId)
            ORDER BY c.[CustomerId];

        OPEN @CustomerCursor;
        FETCH NEXT FROM @CustomerCursor INTO @CurrCustomerId;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @TotalEligible = @TotalEligible + 1;

            -- Check if published bill already exists for this customer & month
            IF EXISTS (
                SELECT 1 FROM [dbo].[MonthlyBills]
                WHERE [CustomerId] = @CurrCustomerId 
                  AND [BillingMonth] = @BillingMonth 
                  AND [BillingYear] = @BillingYear 
                  AND [IsPublished] = 1
            )
            BEGIN
                SET @TotalSkipped = @TotalSkipped + 1;
            END
            ELSE
            BEGIN
                -- Attempt single-customer generation in an isolated block
                BEGIN TRY
                    DECLARE @GeneratedBillId BIGINT;
                    EXEC [dbo].[usp_MonthlyBilling_GenerateCustomerBill]
                        @CustomerId = @CurrCustomerId,
                        @BillingMonth = @BillingMonth,
                        @BillingYear = @BillingYear,
                        @AdminUserId = @AdminUserId,
                        @BillId = @GeneratedBillId OUTPUT;

                    SET @TotalSucceeded = @TotalSucceeded + 1;
                END TRY
                BEGIN CATCH
                    SET @TotalFailed = @TotalFailed + 1;
                END CATCH;
            END;

            FETCH NEXT FROM @CustomerCursor INTO @CurrCustomerId;
        END;

        CLOSE @CustomerCursor;
        DEALLOCATE @CustomerCursor;

        -- Release Application Lock
        EXEC sp_releaseapplock @Resource = 'DMS_BulkBilling_Lock', @LockOwner = 'Session';
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local', '@CustomerCursor') >= 0
        BEGIN
            CLOSE @CustomerCursor;
            DEALLOCATE @CustomerCursor;
        END;

        EXEC sp_releaseapplock @Resource = 'DMS_BulkBilling_Lock', @LockOwner = 'Session';
        THROW;
    END CATCH;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 3: dbo.usp_MonthlyBilling_PublishBill
-- Publishes a draft bill, snapshots its due date, and locks it against direct deletion.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_PublishBill]
    @BillId BIGINT,
    @GraceDays INT NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validate @GraceDays if provided: must be between 1 and 90
    IF @GraceDays IS NOT NULL AND (@GraceDays < 1 OR @GraceDays > 90)
        THROW 50013, 'Grace days must be between 1 and 90 or NULL.', 1;

    BEGIN TRANSACTION;

    DECLARE @IsPublished BIT, @PeriodEndDate DATE, @BalanceAmount DECIMAL(18,2), @PaidAmount DECIMAL(18,2);
    SELECT 
        @IsPublished = [IsPublished],
        @PeriodEndDate = [PeriodEndDate],
        @BalanceAmount = [BalanceAmount],
        @PaidAmount = [PaidAmount]
    FROM [dbo].[MonthlyBills] WITH (UPDLOCK, HOLDLOCK)
    WHERE [BillId] = @BillId;

    IF @PeriodEndDate IS NULL
        THROW 50012, 'Bill not found.', 1;

    IF @IsPublished = 1
        THROW 50011, 'Bill is already published.', 1;

    -- Calculate PaymentDueDate: NULL if @GraceDays is NULL; otherwise DATEADD(day, @GraceDays, @PeriodEndDate)
    DECLARE @PaymentDueDate DATE = NULL;
    IF @GraceDays IS NOT NULL
        SET @PaymentDueDate = DATEADD(day, @GraceDays, @PeriodEndDate);

    -- Determine initial published status
    DECLARE @CurrentISTDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @InitialStatus NVARCHAR(20);

    IF @BalanceAmount <= 0.00
        SET @InitialStatus = 'Paid';
    ELSE IF @PaidAmount = 0.00
        SET @InitialStatus = CASE WHEN @PaymentDueDate IS NOT NULL AND @CurrentISTDate > @PaymentDueDate THEN 'Overdue' ELSE 'Unpaid' END;
    ELSE
        SET @InitialStatus = CASE WHEN @PaymentDueDate IS NOT NULL AND @CurrentISTDate > @PaymentDueDate THEN 'Overdue' ELSE 'Partially Paid' END;

    UPDATE [dbo].[MonthlyBills]
    SET [IsPublished] = 1,
        [PublishedAt] = SYSUTCDATETIME(),
        [PaymentDueDate] = @PaymentDueDate,
        [PaymentStatus] = @InitialStatus,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [BillId] = @BillId;

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 4: dbo.usp_MonthlyBilling_BulkPublish
-- Publishes all draft bills matching the period and optional route filter.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_BulkPublish]
    @BillingMonth TINYINT,
    @BillingYear SMALLINT,
    @RouteId INT NULL,
    @GraceDays INT NULL,
    @AdminUserId INT,
    @PublishedCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    -- Validate @GraceDays if provided: must be between 1 and 90
    IF @GraceDays IS NOT NULL AND (@GraceDays < 1 OR @GraceDays > 90)
        THROW 50013, 'Grace days must be between 1 and 90 or NULL.', 1;

    SET @PublishedCount = 0;

    DECLARE @PublishCursor CURSOR;
    DECLARE @TargetBillId BIGINT;

    SET @PublishCursor = CURSOR LOCAL FAST_FORWARD FOR
        SELECT b.[BillId]
        FROM [dbo].[MonthlyBills] b
        JOIN [dbo].[Customers] c ON b.[CustomerId] = c.[CustomerId]
        WHERE b.[BillingMonth] = @BillingMonth
          AND b.[BillingYear] = @BillingYear
          AND b.[IsPublished] = 0
          AND (@RouteId IS NULL OR c.[RouteId] = @RouteId);

    OPEN @PublishCursor;
    FETCH NEXT FROM @PublishCursor INTO @TargetBillId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            EXEC [dbo].[usp_MonthlyBilling_PublishBill]
                @BillId = @TargetBillId,
                @GraceDays = @GraceDays,
                @AdminUserId = @AdminUserId;

            SET @PublishedCount = @PublishedCount + 1;
        END TRY
        BEGIN CATCH
            -- Continue bulk run if a single bill encounters an error
        END CATCH;

        FETCH NEXT FROM @PublishCursor INTO @TargetBillId;
    END;

    CLOSE @PublishCursor;
    DEALLOCATE @PublishCursor;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 5: dbo.usp_MonthlyBilling_ReviseBill
-- Executes a formal bill revision with deterministic LIFO reversal for downward adjustments.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_ReviseBill]
    @BillId BIGINT,
    @ExpectedVersion INT,
    @Adjustments DECIMAL(18,2),
    @Reason NVARCHAR(500),
    @AdminUserId INT,
    @NewRevisionId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. Lock Target Bill Row with UPDLOCK, HOLDLOCK
    DECLARE @CurrentMilkAmount DECIMAL(18,2), @CurrentInvoiceAmount DECIMAL(18,2);
    DECLARE @PriorAdjustments DECIMAL(18,2), @CurrentPaid DECIMAL(18,2), @CurrentBalance DECIMAL(18,2);
    DECLARE @CurrentVersion INT, @PaymentDueDate DATE, @PreviousBalanceSnapshot DECIMAL(18,2);

    SELECT 
        @CurrentMilkAmount = [CurrentMonthMilkAmount],
        @CurrentInvoiceAmount = [CurrentMonthInvoiceAmount],
        @PriorAdjustments = [Adjustments],
        @CurrentPaid = [PaidAmount],
        @CurrentBalance = [BalanceAmount],
        @CurrentVersion = [Version],
        @PaymentDueDate = [PaymentDueDate],
        @PreviousBalanceSnapshot = [PreviousBalanceSnapshot]
    FROM [dbo].[MonthlyBills] WITH (UPDLOCK, HOLDLOCK)
    WHERE [BillId] = @BillId;

    IF @CurrentMilkAmount IS NULL
        THROW 50029, 'Target bill does not exist.', 1;

    -- 2. Verify Optimistic Concurrency
    IF @CurrentVersion <> @ExpectedVersion
        THROW 50030, 'Concurrency violation: Bill has been modified.', 1;

    -- 3. Calculate New Invoice Target Amount
    DECLARE @TargetInvoiceAmount DECIMAL(18,2) = @CurrentMilkAmount - @Adjustments;
    IF @TargetInvoiceAmount < 0
        THROW 50031, 'Adjustments cannot exceed gross milk amount.', 1;

    -- 4. Calculate Current Active Allocation Directly from Event Ledger
    DECLARE @CurrentActiveAllocation DECIMAL(18,2) = ISNULL((
        SELECT SUM([SignedAmount]) 
        FROM [dbo].[PaymentAllocationEvents] 
        WHERE [BillId] = @BillId
    ), 0.00);

    -- 5. Insert Revision Log into dbo.MonthlyBillRevisions
    INSERT INTO [dbo].[MonthlyBillRevisions] (
        [BillId],
        [VersionNumber],
        [PriorMilkAmount],
        [NewMilkAmount],
        [PriorAdjustments],
        [NewAdjustments],
        [PriorInvoiceAmount],
        [NewInvoiceAmount],
        [PreviousBalanceSnapshot],
        [PaidAmountSnapshot],
        [PriorBalanceAmount],
        [NewBalanceAmount],
        [Reason],
        [RevisedBy],
        [RevisedAt]
    ) VALUES (
        @BillId,
        @CurrentVersion,
        @CurrentMilkAmount,
        @CurrentMilkAmount,
        @PriorAdjustments,
        @Adjustments,
        @CurrentInvoiceAmount,
        @TargetInvoiceAmount,
        @PreviousBalanceSnapshot,
        @CurrentPaid,
        @CurrentBalance,
        @TargetInvoiceAmount - @CurrentPaid,
        @Reason,
        @AdminUserId,
        SYSUTCDATETIME()
    );

    SET @NewRevisionId = SCOPE_IDENTITY();

    -- 6. Evaluate Allocation Adjustment Path
    DECLARE @FinalPaidAmount DECIMAL(18,2);
    DECLARE @FinalBalanceAmount DECIMAL(18,2);

    IF @TargetInvoiceAmount < @CurrentActiveAllocation
    BEGIN
        -- Downward revision releases over-allocated funds in strict LIFO order
        DECLARE @ExcessToRelease DECIMAL(18,2) = @CurrentActiveAllocation - @TargetInvoiceAmount;

        DECLARE @LIFOCursor CURSOR;
        DECLARE @CandEventId BIGINT, @CandPaymentId BIGINT, @CandActiveRemaining DECIMAL(18,2);

        SET @LIFOCursor = CURSOR LOCAL FAST_FORWARD FOR
            SELECT 
                e.[AllocationEventId], 
                e.[PaymentId], 
                e.[Amount] - ISNULL(prior_rev.[ReversedAmount], 0.00) AS RemainingActiveAmount
            FROM [dbo].[PaymentAllocationEvents] e
            LEFT JOIN (
                SELECT [OriginalAllocationEventId], SUM([Amount]) AS ReversedAmount
                FROM [dbo].[PaymentAllocationEvents]
                WHERE [BillId] = @BillId AND [Direction] = -1
                GROUP BY [OriginalAllocationEventId]
            ) prior_rev ON e.[AllocationEventId] = prior_rev.[OriginalAllocationEventId]
            WHERE e.[BillId] = @BillId 
              AND e.[Direction] = 1
              AND (e.[Amount] - ISNULL(prior_rev.[ReversedAmount], 0.00)) > 0.00
            ORDER BY e.[AllocationEventId] DESC;  -- Deterministic LIFO order

        OPEN @LIFOCursor;
        FETCH NEXT FROM @LIFOCursor INTO @CandEventId, @CandPaymentId, @CandActiveRemaining;

        WHILE @ExcessToRelease > 0 AND @@FETCH_STATUS = 0
        BEGIN
            DECLARE @ReversalPortion DECIMAL(18,2) = CASE 
                WHEN @CandActiveRemaining < @ExcessToRelease THEN @CandActiveRemaining 
                ELSE @ExcessToRelease 
            END;

            INSERT INTO [dbo].[PaymentAllocationEvents] (
                [PaymentId],
                [BillId],
                [EventType],
                [OriginalAllocationEventId],
                [Amount],
                [Direction],
                [Reason],
                [RelatedRevisionId],
                [LegacyAllocationId],
                [PerformedBy],
                [PerformedAt]
            ) VALUES (
                @CandPaymentId,
                @BillId,
                'ReversalAdjustment',
                @CandEventId,
                @ReversalPortion,
                -1,
                @Reason,
                @NewRevisionId,
                NULL,
                @AdminUserId,
                SYSUTCDATETIME()
            );

            SET @ExcessToRelease = @ExcessToRelease - @ReversalPortion;
            FETCH NEXT FROM @LIFOCursor INTO @CandEventId, @CandPaymentId, @CandActiveRemaining;
        END;

        CLOSE @LIFOCursor;
        DEALLOCATE @LIFOCursor;

        -- Strict Event-Ledger Verification Assertion
        DECLARE @RecalculatedPaid DECIMAL(18,2) = ISNULL((
            SELECT SUM([SignedAmount]) 
            FROM [dbo].[PaymentAllocationEvents] 
            WHERE [BillId] = @BillId
        ), 0.00);

        IF @RecalculatedPaid <> @TargetInvoiceAmount
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50065, 'Reversal reconciliation failed: Event ledger active total does not equal target invoice amount.', 1;
        END;

        SET @FinalPaidAmount = @RecalculatedPaid;
        SET @FinalBalanceAmount = @TargetInvoiceAmount - @FinalPaidAmount;  -- (0.00)
    END
    ELSE
    BEGIN
        -- Upward or equal revision: no reversal required
        SET @FinalPaidAmount = @CurrentActiveAllocation;
        SET @FinalBalanceAmount = @TargetInvoiceAmount - @FinalPaidAmount;
    END;

    -- 7. Derive Authoritative Payment Status
    DECLARE @CurrentISTDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @NewStatus NVARCHAR(20);

    IF @FinalBalanceAmount <= 0.00
        SET @NewStatus = 'Paid';
    ELSE IF @FinalPaidAmount = 0.00
        SET @NewStatus = CASE WHEN @PaymentDueDate IS NOT NULL AND @CurrentISTDate > @PaymentDueDate THEN 'Overdue' ELSE 'Unpaid' END;
    ELSE
        SET @NewStatus = CASE WHEN @PaymentDueDate IS NOT NULL AND @CurrentISTDate > @PaymentDueDate THEN 'Overdue' ELSE 'Partially Paid' END;

    -- 8. Atomically Update MonthlyBills
    UPDATE [dbo].[MonthlyBills]
    SET [CurrentMonthInvoiceAmount] = @TargetInvoiceAmount,
        [Adjustments] = @Adjustments,
        [PaidAmount] = @FinalPaidAmount,
        [BalanceAmount] = @FinalBalanceAmount,
        [PaymentStatus] = @NewStatus,
        [Version] = [Version] + 1,
        [RevisionReason] = @Reason,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [BillId] = @BillId;

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 6: dbo.usp_Payment_RecordAndAllocateSingle
-- Records a customer payment receipt and allocates strictly to ONE selected invoice.
-- Excess amount is retained as unallocated advance credit on that payment receipt.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Payment_RecordAndAllocateSingle]
    @CustomerId INT,
    @Amount DECIMAL(18,2),
    @PaymentMode NVARCHAR(20),
    @ReferenceNumber NVARCHAR(100),
    @Notes NVARCHAR(500),
    @SelectedBillId BIGINT,
    @AdminUserId INT,
    @NewPaymentId BIGINT OUTPUT,
    @NewAllocationEventId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewAllocationEventId = NULL;

    IF @Amount <= 0
        THROW 50033, 'Payment amount must be greater than zero.', 1;

    IF @PaymentMode NOT IN ('Online', 'Offline')
        THROW 50034, 'PaymentMode must be strictly Online or Offline.', 1;

    BEGIN TRANSACTION;

    -- 1. Validate Target Bill & Customer Match with UPDLOCK, HOLDLOCK
    DECLARE @BillCustomerId INT, @BillBalance DECIMAL(18,2), @BillInvoiceAmt DECIMAL(18,2);
    DECLARE @BillIsPublished BIT, @BillDueDate DATE;

    SELECT 
        @BillCustomerId = [CustomerId],
        @BillBalance = [BalanceAmount],
        @BillInvoiceAmt = [CurrentMonthInvoiceAmount],
        @BillIsPublished = [IsPublished],
        @BillDueDate = [PaymentDueDate]
    FROM [dbo].[MonthlyBills] WITH (UPDLOCK, HOLDLOCK)
    WHERE [BillId] = @SelectedBillId;

    IF @BillCustomerId IS NULL
        THROW 50038, 'Selected invoice does not exist.', 1;

    IF @BillCustomerId <> @CustomerId
        THROW 50035, 'Selected invoice does not belong to the paying customer.', 1;

    IF @BillIsPublished = 0
        THROW 50039, 'Cannot allocate payment to an unpublished draft bill.', 1;

    -- 2. Generate Deterministic Receipt Number
    DECLARE @CurrentDate DATE = CAST(SYSUTCDATETIME() AS DATE);
    DECLARE @DatePrefix NVARCHAR(12) = N'REC-' + FORMAT(@CurrentDate, 'yyyyMMdd') + N'-';
    DECLARE @Seq INT = ISNULL((
        SELECT COUNT(*) + 1 
        FROM [dbo].[Payments] 
        WHERE [ReceiptNumber] LIKE @DatePrefix + '%'
    ), 1);
    DECLARE @ReceiptNumber NVARCHAR(30) = @DatePrefix + RIGHT('0000' + CAST(@Seq AS NVARCHAR(10)), 4);

    -- 3. Insert Payment Receipt
    INSERT INTO [dbo].[Payments] (
        [ReceiptNumber],
        [CustomerId],
        [PaymentDate],
        [Amount],
        [PaymentMode],
        [ReferenceNumber],
        [Notes],
        [IsVoided],
        [ReceivedBy],
        [CreatedAt]
    ) VALUES (
        @ReceiptNumber,
        @CustomerId,
        @CurrentDate,
        @Amount,
        @PaymentMode,
        @ReferenceNumber,
        @Notes,
        0,
        @AdminUserId,
        SYSUTCDATETIME()
    );

    SET @NewPaymentId = SCOPE_IDENTITY();

    -- Log Payment in PaymentAuditLogs
    INSERT INTO [dbo].[PaymentAuditLogs] (
        [PaymentId], [Action], [OldAmount], [NewAmount], [OldStatus], [NewStatus], [Reason], [PerformedBy]
    ) VALUES (
        @NewPaymentId, 'PaymentRecorded', NULL, @Amount, NULL, 'Active', 'Payment recorded by admin', @AdminUserId
    );

    -- 4. Calculate Allocation Portion to Selected Bill
    DECLARE @AllocPortion DECIMAL(18,2) = CASE 
        WHEN @Amount < @BillBalance THEN @Amount 
        ELSE @BillBalance 
    END;

    IF @AllocPortion > 0
    BEGIN
        -- Insert Initial Allocation Event
        INSERT INTO [dbo].[PaymentAllocationEvents] (
            [PaymentId],
            [BillId],
            [EventType],
            [OriginalAllocationEventId],
            [Amount],
            [Direction],
            [Reason],
            [RelatedRevisionId],
            [LegacyAllocationId],
            [PerformedBy],
            [PerformedAt]
        ) VALUES (
            @NewPaymentId,
            @SelectedBillId,
            'InitialAllocation',
            NULL,
            @AllocPortion,
            1,
            'Payment receipt allocated to invoice',
            NULL,
            NULL,
            @AdminUserId,
            SYSUTCDATETIME()
        );

        SET @NewAllocationEventId = SCOPE_IDENTITY();

        -- 5. Recalculate Selected Invoice Balances and Payment Status
        DECLARE @RecalcPaid DECIMAL(18,2) = ISNULL((
            SELECT SUM([SignedAmount]) 
            FROM [dbo].[PaymentAllocationEvents] 
            WHERE [BillId] = @SelectedBillId
        ), 0.00);

        DECLARE @RecalcBalance DECIMAL(18,2) = @BillInvoiceAmt - @RecalcPaid;
        DECLARE @CurrentISTDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
        DECLARE @NewBillStatus NVARCHAR(20);

        IF @RecalcBalance <= 0.00
            SET @NewBillStatus = 'Paid';
        ELSE IF @RecalcPaid = 0.00
            SET @NewBillStatus = CASE WHEN @BillDueDate IS NOT NULL AND @CurrentISTDate > @BillDueDate THEN 'Overdue' ELSE 'Unpaid' END;
        ELSE
            SET @NewBillStatus = CASE WHEN @BillDueDate IS NOT NULL AND @CurrentISTDate > @BillDueDate THEN 'Overdue' ELSE 'Partially Paid' END;

        UPDATE [dbo].[MonthlyBills]
        SET [PaidAmount] = @RecalcPaid,
            [BalanceAmount] = @RecalcBalance,
            [PaymentStatus] = @NewBillStatus,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [BillId] = @SelectedBillId;
    END;

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 7: dbo.usp_Payment_AllocateAdvanceCreditSingle
-- Allocates existing unallocated advance credit from a payment to ONE selected bill.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Payment_AllocateAdvanceCreditSingle]
    @PaymentId BIGINT,
    @SelectedBillId BIGINT,
    @AllocatedAmount DECIMAL(18,2),
    @AdminUserId INT,
    @NewAllocationEventId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @AllocatedAmount <= 0
        THROW 50032, 'Allocated advance credit amount must be greater than zero.', 1;

    BEGIN TRANSACTION;

    -- 1. Lock Payment Row with UPDLOCK, HOLDLOCK
    DECLARE @PayCustomerId INT, @PaymentAmount DECIMAL(18,2), @IsVoided BIT;
    SELECT 
        @PayCustomerId = [CustomerId],
        @PaymentAmount = [Amount],
        @IsVoided = [IsVoided]
    FROM [dbo].[Payments] WITH (UPDLOCK, HOLDLOCK)
    WHERE [PaymentId] = @PaymentId;

    IF @PaymentAmount IS NULL
        THROW 50040, 'Payment record does not exist.', 1;

    IF @IsVoided = 1
        THROW 50042, 'Cannot allocate advance credit from a voided payment receipt.', 1;

    -- 2. Lock Target Bill Row with UPDLOCK, HOLDLOCK
    DECLARE @BillCustomerId INT, @BillBalance DECIMAL(18,2), @BillInvoiceAmt DECIMAL(18,2);
    DECLARE @BillIsPublished BIT, @BillDueDate DATE;

    SELECT 
        @BillCustomerId = [CustomerId],
        @BillBalance = [BalanceAmount],
        @BillInvoiceAmt = [CurrentMonthInvoiceAmount],
        @BillIsPublished = [IsPublished],
        @BillDueDate = [PaymentDueDate]
    FROM [dbo].[MonthlyBills] WITH (UPDLOCK, HOLDLOCK)
    WHERE [BillId] = @SelectedBillId;

    IF @BillCustomerId IS NULL
        THROW 50038, 'Selected invoice does not exist.', 1;

    IF @BillCustomerId <> @PayCustomerId
        THROW 50035, 'Target bill belongs to a different customer than the payment receipt.', 1;

    IF @BillIsPublished = 0
        THROW 50039, 'Cannot allocate advance credit to an unpublished draft bill.', 1;

    -- 3. Calculate Available Advance Credit on Payment
    DECLARE @TotalAllocatedOnPayment DECIMAL(18,2) = ISNULL((
        SELECT SUM([SignedAmount])
        FROM [dbo].[PaymentAllocationEvents]
        WHERE [PaymentId] = @PaymentId
    ), 0.00);

    DECLARE @AvailableCredit DECIMAL(18,2) = @PaymentAmount - @TotalAllocatedOnPayment;

    IF @AllocatedAmount > @AvailableCredit
        THROW 50036, 'Allocated amount exceeds available advance credit on this payment receipt.', 1;

    IF @AllocatedAmount > @BillBalance
        THROW 50037, 'Allocated amount exceeds outstanding balance of target bill.', 1;

    -- 4. Insert AdvanceCreditAllocation Event
    INSERT INTO [dbo].[PaymentAllocationEvents] (
        [PaymentId],
        [BillId],
        [EventType],
        [OriginalAllocationEventId],
        [Amount],
        [Direction],
        [Reason],
        [RelatedRevisionId],
        [LegacyAllocationId],
        [PerformedBy],
        [PerformedAt]
    ) VALUES (
        @PaymentId,
        @SelectedBillId,
        'AdvanceCreditAllocation',
        NULL,
        @AllocatedAmount,
        1,
        'Advance credit allocation to invoice',
        NULL,
        NULL,
        @AdminUserId,
        SYSUTCDATETIME()
    );

    SET @NewAllocationEventId = SCOPE_IDENTITY();

    -- Log in PaymentAuditLogs
    INSERT INTO [dbo].[PaymentAuditLogs] (
        [PaymentId], [Action], [OldAmount], [NewAmount], [OldStatus], [NewStatus], [Reason], [PerformedBy]
    ) VALUES (
        @PaymentId, 'AdvanceCreditAllocated', @AvailableCredit, @AvailableCredit - @AllocatedAmount, 'Active', 'Active', 'Advance credit allocated to bill ' + CAST(@SelectedBillId AS NVARCHAR), @AdminUserId
    );

    -- 5. Recalculate Target Bill Balances and Status
    DECLARE @RecalcPaid DECIMAL(18,2) = ISNULL((
        SELECT SUM([SignedAmount]) 
        FROM [dbo].[PaymentAllocationEvents] 
        WHERE [BillId] = @SelectedBillId
    ), 0.00);

    DECLARE @RecalcBalance DECIMAL(18,2) = @BillInvoiceAmt - @RecalcPaid;
    DECLARE @CurrentISTDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @NewBillStatus NVARCHAR(20);

    IF @RecalcBalance <= 0.00
        SET @NewBillStatus = 'Paid';
    ELSE IF @RecalcPaid = 0.00
        SET @NewBillStatus = CASE WHEN @BillDueDate IS NOT NULL AND @CurrentISTDate > @BillDueDate THEN 'Overdue' ELSE 'Unpaid' END;
    ELSE
        SET @NewBillStatus = CASE WHEN @BillDueDate IS NOT NULL AND @CurrentISTDate > @BillDueDate THEN 'Overdue' ELSE 'Partially Paid' END;

    UPDATE [dbo].[MonthlyBills]
    SET [PaidAmount] = @RecalcPaid,
        [BalanceAmount] = @RecalcBalance,
        [PaymentStatus] = @NewBillStatus,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [BillId] = @SelectedBillId;

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 8: dbo.usp_Payment_Void
-- Two-tiered concurrency-safe idempotency voiding of a payment receipt.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Payment_Void]
    @PaymentId BIGINT,
    @VoidReason NVARCHAR(500),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @VoidReason IS NULL OR LEN(LTRIM(RTRIM(@VoidReason))) < 10
        THROW 50045, 'VoidReason is mandatory and must be at least 10 characters in length.', 1;

    BEGIN TRANSACTION;

    -- Tier 1 Concurrency-Safe Payment Row Lock & Idempotency Check
    DECLARE @CustomerId INT, @PaymentAmount DECIMAL(18,2), @IsVoided BIT;
    SELECT 
        @CustomerId = [CustomerId],
        @PaymentAmount = [Amount],
        @IsVoided = [IsVoided]
    FROM [dbo].[Payments] WITH (UPDLOCK, HOLDLOCK)
    WHERE [PaymentId] = @PaymentId;

    IF @PaymentAmount IS NULL
        THROW 50040, 'Payment record does not exist.', 1;

    IF @IsVoided = 1
        THROW 50041, 'Payment is already voided. Repeated void operations are strictly prohibited.', 1;

    -- Identify Affected Invoices and Remaining Active Allocations
    DECLARE @AllocationsCursor CURSOR;
    DECLARE @TargetBillId BIGINT, @InitialEventId BIGINT, @RemainingActiveAmount DECIMAL(18,2);

    SET @AllocationsCursor = CURSOR LOCAL FAST_FORWARD FOR
        SELECT 
            e.[BillId],
            e.[AllocationEventId],
            e.[Amount] - ISNULL(reversals.[ReversedAmount], 0.00) AS RemainingActiveAmount
        FROM [dbo].[PaymentAllocationEvents] e
        LEFT JOIN (
            SELECT [OriginalAllocationEventId], SUM([Amount]) AS ReversedAmount
            FROM [dbo].[PaymentAllocationEvents]
            WHERE [PaymentId] = @PaymentId AND [Direction] = -1
            GROUP BY [OriginalAllocationEventId]
        ) reversals ON e.[AllocationEventId] = reversals.[OriginalAllocationEventId]
        WHERE e.[PaymentId] = @PaymentId 
          AND e.[Direction] = 1
          AND (e.[Amount] - ISNULL(reversals.[ReversedAmount], 0.00)) > 0.00;

    OPEN @AllocationsCursor;
    FETCH NEXT FROM @AllocationsCursor INTO @TargetBillId, @InitialEventId, @RemainingActiveAmount;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Tier 2 Storage-Guarded VoidReversal Insertion
        INSERT INTO [dbo].[PaymentAllocationEvents] (
            [PaymentId],
            [BillId],
            [EventType],
            [OriginalAllocationEventId],
            [Amount],
            [Direction],
            [Reason],
            [RelatedRevisionId],
            [LegacyAllocationId],
            [PerformedBy],
            [PerformedAt]
        ) VALUES (
            @PaymentId,
            @TargetBillId,
            'VoidReversal',
            @InitialEventId,
            @RemainingActiveAmount,
            -1,
            @VoidReason,
            NULL,
            NULL,
            @AdminUserId,
            SYSUTCDATETIME()
        );

        -- Recalculate Affected Invoice Balances & Statuses
        DECLARE @InvoiceAmount DECIMAL(18,2), @DueDate DATE;
        SELECT 
            @InvoiceAmount = [CurrentMonthInvoiceAmount], 
            @DueDate = [PaymentDueDate]
        FROM [dbo].[MonthlyBills] WITH (UPDLOCK, HOLDLOCK)
        WHERE [BillId] = @TargetBillId;

        DECLARE @RecalcPaid DECIMAL(18,2) = ISNULL((
            SELECT SUM([SignedAmount]) 
            FROM [dbo].[PaymentAllocationEvents] 
            WHERE [BillId] = @TargetBillId
        ), 0.00);

        DECLARE @RecalcBalance DECIMAL(18,2) = @InvoiceAmount - @RecalcPaid;
        DECLARE @CurrentISTDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
        DECLARE @NewStatus NVARCHAR(20);

        IF @RecalcBalance <= 0.00
            SET @NewStatus = 'Paid';
        ELSE IF @RecalcPaid = 0.00
            SET @NewStatus = CASE WHEN @DueDate IS NOT NULL AND @CurrentISTDate > @DueDate THEN 'Overdue' ELSE 'Unpaid' END;
        ELSE
            SET @NewStatus = CASE WHEN @DueDate IS NOT NULL AND @CurrentISTDate > @DueDate THEN 'Overdue' ELSE 'Partially Paid' END;

        UPDATE [dbo].[MonthlyBills]
        SET [PaidAmount] = @RecalcPaid,
            [BalanceAmount] = @RecalcBalance,
            [PaymentStatus] = @NewStatus,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [BillId] = @TargetBillId;

        FETCH NEXT FROM @AllocationsCursor INTO @TargetBillId, @InitialEventId, @RemainingActiveAmount;
    END;

    CLOSE @AllocationsCursor;
    DEALLOCATE @AllocationsCursor;

    -- Update Payments Table Status within Same Transaction
    UPDATE [dbo].[Payments]
    SET [IsVoided] = 1,
        [VoidedAt] = SYSUTCDATETIME(),
        [VoidReason] = @VoidReason,
        [VoidedBy] = @AdminUserId
    WHERE [PaymentId] = @PaymentId;

    -- Log in PaymentAuditLogs
    INSERT INTO [dbo].[PaymentAuditLogs] (
        [PaymentId], [Action], [OldAmount], [NewAmount], [OldStatus], [NewStatus], [Reason], [PerformedBy]
    ) VALUES (
        @PaymentId, 'PaymentVoided', @PaymentAmount, 0.00, 'Active', 'Voided', @VoidReason, @AdminUserId
    );

    COMMIT TRANSACTION;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 9: dbo.usp_Payment_GetCustomerOutstandingBills
-- Returns published outstanding bills and total available advance credit for a customer.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Payment_GetCustomerOutstandingBills]
    @CustomerId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result Set 1: Outstanding Published Bills
    SELECT 
        b.[BillId],
        b.[BillNumber],
        b.[CustomerId],
        b.[BillingMonth],
        b.[BillingYear],
        b.[PeriodStartDate],
        b.[PeriodEndDate],
        b.[TotalLiquidMilkVolumeLitres],
        b.[CurrentMonthInvoiceAmount],
        b.[PaidAmount],
        b.[BalanceAmount],
        b.[PaymentStatus],
        b.[PaymentDueDate]
    FROM [dbo].[MonthlyBills] b
    WHERE b.[CustomerId] = @CustomerId
      AND b.[IsPublished] = 1
      AND b.[BalanceAmount] > 0.00
    ORDER BY b.[BillingYear] ASC, b.[BillingMonth] ASC;

    -- Result Set 2: Available Advance Credit Summary across non-voided payments
    SELECT 
        ISNULL(SUM(p.[Amount] - ISNULL(alloc.[AllocatedTotal], 0.00)), 0.00) AS TotalAvailableAdvanceCredit
    FROM [dbo].[Payments] p
    LEFT JOIN (
        SELECT [PaymentId], SUM([SignedAmount]) AS AllocatedTotal
        FROM [dbo].[PaymentAllocationEvents]
        GROUP BY [PaymentId]
    ) alloc ON p.[PaymentId] = alloc.[PaymentId]
    WHERE p.[CustomerId] = @CustomerId
      AND p.[IsVoided] = 0
      AND (p.[Amount] - ISNULL(alloc.[AllocatedTotal], 0.00)) > 0.00;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 10: dbo.usp_MonthlyBilling_GetBillsContinuous & dbo.usp_MonthlyBilling_GetBillDetails
-- Paginated bill listing and comprehensive bill detail viewer.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_GetBillsContinuous]
    @BillingMonth TINYINT = NULL,
    @BillingYear SMALLINT = NULL,
    @RouteId INT = NULL,
    @PaymentStatus NVARCHAR(20) = NULL,
    @SearchText NVARCHAR(100) = NULL,
    @Offset INT = 0,
    @Limit INT = 25
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Total Count Matching Criteria
    SELECT COUNT(*) AS TotalCount
    FROM [dbo].[MonthlyBills] b
    JOIN [dbo].[Customers] c ON b.[CustomerId] = c.[CustomerId]
    LEFT JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    WHERE (@BillingMonth IS NULL OR b.[BillingMonth] = @BillingMonth)
      AND (@BillingYear IS NULL OR b.[BillingYear] = @BillingYear)
      AND (@RouteId IS NULL OR c.[RouteId] = @RouteId)
      AND (@PaymentStatus IS NULL OR b.[PaymentStatus] = @PaymentStatus)
      AND (@SearchText IS NULL OR (c.[FullName] LIKE '%' + @SearchText + '%' OR c.[Phone] LIKE '%' + @SearchText + '%' OR b.[BillNumber] LIKE '%' + @SearchText + '%'));

    -- 2. Paginated Data Set
    SELECT 
        b.[BillId],
        b.[BillNumber],
        b.[CustomerId],
        c.[FullName] AS CustomerName,
        c.[Phone] AS CustomerPhone,
        r.[RouteName],
        b.[BillingMonth],
        b.[BillingYear],
        b.[PeriodStartDate],
        b.[PeriodEndDate],
        b.[TotalLiquidMilkVolumeLitres],
        b.[CurrentMonthMilkAmount],
        b.[Adjustments],
        b.[CurrentMonthInvoiceAmount],
        b.[PreviousBalanceSnapshot],
        b.[TotalStatementAmount],
        b.[PaidAmount],
        b.[BalanceAmount],
        b.[PaymentStatus],
        b.[IsPublished],
        b.[PublishedAt],
        b.[PaymentDueDate],
        b.[Version],
        b.[RevisionReason],
        b.[CreatedAt],
        b.[CreatedBy]
    FROM [dbo].[MonthlyBills] b
    JOIN [dbo].[Customers] c ON b.[CustomerId] = c.[CustomerId]
    LEFT JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    WHERE (@BillingMonth IS NULL OR b.[BillingMonth] = @BillingMonth)
      AND (@BillingYear IS NULL OR b.[BillingYear] = @BillingYear)
      AND (@RouteId IS NULL OR c.[RouteId] = @RouteId)
      AND (@PaymentStatus IS NULL OR b.[PaymentStatus] = @PaymentStatus)
      AND (@SearchText IS NULL OR (c.[FullName] LIKE '%' + @SearchText + '%' OR c.[Phone] LIKE '%' + @SearchText + '%' OR b.[BillNumber] LIKE '%' + @SearchText + '%'))
    ORDER BY b.[BillingYear] DESC, b.[BillingMonth] DESC, b.[BillId] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @Limit ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_MonthlyBilling_GetBillDetails]
    @BillId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result Set 1: Bill Header & Customer Information
    SELECT 
        b.[BillId],
        b.[BillNumber],
        b.[CustomerId],
        c.[FullName] AS CustomerName,
        c.[Phone] AS CustomerPhone,
        c.[Address] AS CustomerAddress,
        r.[RouteName],
        b.[BillingMonth],
        b.[BillingYear],
        b.[PeriodStartDate],
        b.[PeriodEndDate],
        b.[TotalLiquidMilkVolumeLitres],
        b.[CurrentMonthMilkAmount],
        b.[Adjustments],
        b.[CurrentMonthInvoiceAmount],
        b.[PreviousBalanceSnapshot],
        b.[TotalStatementAmount],
        b.[PaidAmount],
        b.[BalanceAmount],
        b.[PaymentStatus],
        b.[IsPublished],
        b.[PublishedAt],
        b.[PaymentDueDate],
        b.[Version],
        b.[RevisionReason],
        b.[CreatedAt],
        b.[CreatedBy],
        u.[FullName] AS CreatedByUserName,
        b.[UpdatedAt],
        b.[UpdatedBy]
    FROM [dbo].[MonthlyBills] b
    JOIN [dbo].[Customers] c ON b.[CustomerId] = c.[CustomerId]
    LEFT JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    LEFT JOIN [dbo].[Users] u ON b.[CreatedBy] = u.[UserId]
    WHERE b.[BillId] = @BillId;

    -- Result Set 2: Itemized Lines from MonthlyBillItems
    SELECT 
        bi.[BillItemId],
        bi.[BillId],
        bi.[CategoryId] AS MilkCategoryId,
        mc.[CategoryName],
        bi.[ProductTypeSnapshot],
        bi.[UnitOfMeasureSnapshot],
        bi.[PricingBasisSnapshot],
        bi.[BillingUnitEquivalentSnapshot],
        bi.[LitreEquivalentSnapshot],
        bi.[UnitPriceSnapshot],
        bi.[DeliveredDaysCount],
        bi.[TotalPackagingQuantity] AS QuantityCount,
        bi.[TotalLiquidVolumeLitres] AS LiquidVolumeLitres,
        bi.[SubTotal]
    FROM [dbo].[MonthlyBillItems] bi
    JOIN [dbo].[MilkCategories] mc ON bi.[CategoryId] = mc.[CategoryId]
    WHERE bi.[BillId] = @BillId
    ORDER BY mc.[CategoryName] ASC;

    -- Result Set 3: Revision History from MonthlyBillRevisions
    SELECT 
        r.[RevisionId],
        r.[BillId],
        r.[VersionNumber],
        r.[PriorMilkAmount],
        r.[NewMilkAmount],
        r.[PriorAdjustments],
        r.[NewAdjustments],
        r.[PriorInvoiceAmount],
        r.[NewInvoiceAmount],
        r.[PreviousBalanceSnapshot],
        r.[PaidAmountSnapshot],
        r.[PriorBalanceAmount],
        r.[NewBalanceAmount],
        r.[Reason],
        r.[RevisedBy],
        u.[FullName] AS RevisedByUserName,
        r.[RevisedAt]
    FROM [dbo].[MonthlyBillRevisions] r
    LEFT JOIN [dbo].[Users] u ON r.[RevisedBy] = u.[UserId]
    WHERE r.[BillId] = @BillId
    ORDER BY r.[VersionNumber] ASC;

    -- Result Set 4: Allocation Events Ledger for this Bill
    SELECT 
        e.[AllocationEventId],
        e.[PaymentId],
        p.[ReceiptNumber],
        p.[PaymentMode],
        e.[BillId],
        b.[BillNumber],
        e.[EventType],
        e.[OriginalAllocationEventId],
        e.[Amount],
        e.[Direction],
        e.[SignedAmount],
        e.[Reason],
        e.[RelatedRevisionId],
        e.[LegacyAllocationId],
        e.[PerformedBy],
        u.[FullName] AS PerformedByUserName,
        e.[PerformedAt]
    FROM [dbo].[PaymentAllocationEvents] e
    JOIN [dbo].[Payments] p ON e.[PaymentId] = p.[PaymentId]
    JOIN [dbo].[MonthlyBills] b ON e.[BillId] = b.[BillId]
    LEFT JOIN [dbo].[Users] u ON e.[PerformedBy] = u.[UserId]
    WHERE e.[BillId] = @BillId
    ORDER BY e.[AllocationEventId] ASC;
END;
GO

-- ------------------------------------------------------------------------------------
-- Procedure 11: dbo.usp_Payment_GetHistory & dbo.usp_BillingReports_GetMonthlySummary
-- Payment history and monthly reporting metrics.
-- ------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_Payment_GetHistory]
    @CustomerId INT = NULL,
    @StartDate DATE = NULL,
    @EndDate DATE = NULL,
    @PaymentMode NVARCHAR(20) = NULL,
    @IsVoided BIT = NULL,
    @Offset INT = 0,
    @Limit INT = 25
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Total Count
    SELECT COUNT(*) AS TotalCount
    FROM [dbo].[Payments] p
    JOIN [dbo].[Customers] c ON p.[CustomerId] = c.[CustomerId]
    WHERE (@CustomerId IS NULL OR p.[CustomerId] = @CustomerId)
      AND (@StartDate IS NULL OR p.[PaymentDate] >= @StartDate)
      AND (@EndDate IS NULL OR p.[PaymentDate] <= @EndDate)
      AND (@PaymentMode IS NULL OR p.[PaymentMode] = @PaymentMode)
      AND (@IsVoided IS NULL OR p.[IsVoided] = @IsVoided);

    -- 2. Paginated Payments with Calculated Active Allocations & Advance Credit
    SELECT 
        p.[PaymentId],
        p.[ReceiptNumber],
        p.[CustomerId],
        c.[FullName] AS CustomerName,
        c.[Phone] AS CustomerPhone,
        p.[PaymentDate],
        p.[Amount],
        p.[PaymentMode],
        p.[ReferenceNumber],
        p.[Notes],
        p.[IsVoided],
        p.[VoidedAt],
        p.[VoidReason],
        p.[ReceivedBy],
        u.[FullName] AS ReceivedByUserName,
        ISNULL(alloc.[AllocatedAmount], 0.00) AS AllocatedAmount,
        ISNULL(alloc.[AllocatedAmount], 0.00) AS ActiveAllocatedAmount,
        CASE 
            WHEN p.[IsVoided] = 1 THEN 0.00 
            ELSE (p.[Amount] - ISNULL(alloc.[AllocatedAmount], 0.00)) 
        END AS AvailableAdvanceCredit,
        p.[CreatedAt]
    FROM [dbo].[Payments] p
    JOIN [dbo].[Customers] c ON p.[CustomerId] = c.[CustomerId]
    LEFT JOIN [dbo].[Users] u ON p.[ReceivedBy] = u.[UserId]
    LEFT JOIN (
        SELECT [PaymentId], SUM([SignedAmount]) AS AllocatedAmount
        FROM [dbo].[PaymentAllocationEvents]
        GROUP BY [PaymentId]
    ) alloc ON p.[PaymentId] = alloc.[PaymentId]
    WHERE (@CustomerId IS NULL OR p.[CustomerId] = @CustomerId)
      AND (@StartDate IS NULL OR p.[PaymentDate] >= @StartDate)
      AND (@EndDate IS NULL OR p.[PaymentDate] <= @EndDate)
      AND (@PaymentMode IS NULL OR p.[PaymentMode] = @PaymentMode)
      AND (@IsVoided IS NULL OR p.[IsVoided] = @IsVoided)
    ORDER BY p.[PaymentDate] DESC, p.[PaymentId] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @Limit ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_BillingReports_GetMonthlySummary]
    @Month TINYINT,
    @Year SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PeriodStartDate DATE = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @PeriodEndDate DATE = EOMONTH(@PeriodStartDate);

    -- 1. High-Level Summary Metrics
    SELECT 
        ISNULL(SUM(b.[TotalLiquidMilkVolumeLitres]), 0.000) AS TotalLiquidMilkVolumeLitres,
        ISNULL(SUM(b.[CurrentMonthMilkAmount]), 0.00) AS TotalCurrentMonthMilkAmount,
        ISNULL(SUM(b.[Adjustments]), 0.00) AS TotalAdjustments,
        ISNULL(SUM(b.[CurrentMonthInvoiceAmount]), 0.00) AS TotalCurrentMonthInvoiceAmount,
        ISNULL(SUM(b.[PaidAmount]), 0.00) AS TotalPaidAmount,
        ISNULL(SUM(b.[BalanceAmount]), 0.00) AS TotalOutstandingBalance,
        COUNT(b.[BillId]) AS TotalBillsCount,
        ISNULL(SUM(CASE WHEN b.[PaymentStatus] = 'Paid' THEN 1 ELSE 0 END), 0) AS PaidBillsCount,
        ISNULL(SUM(CASE WHEN b.[PaymentStatus] = 'Partially Paid' THEN 1 ELSE 0 END), 0) AS PartiallyPaidBillsCount,
        ISNULL(SUM(CASE WHEN b.[PaymentStatus] = 'Unpaid' THEN 1 ELSE 0 END), 0) AS UnpaidBillsCount,
        ISNULL(SUM(CASE WHEN b.[PaymentStatus] = 'Overdue' THEN 1 ELSE 0 END), 0) AS OverdueBillsCount
    FROM [dbo].[MonthlyBills] b
    WHERE b.[BillingMonth] = @Month AND b.[BillingYear] = @Year AND b.[IsPublished] = 1;

    -- 2. Route-Wise Performance Breakdown for target month
    SELECT 
        r.[RouteId],
        r.[RouteName],
        COUNT(b.[BillId]) AS TotalCustomers,
        ISNULL(SUM(b.[CurrentMonthInvoiceAmount]), 0.00) AS BilledAmount,
        ISNULL(SUM(b.[PaidAmount]), 0.00) AS CollectedAmount,
        ISNULL(SUM(b.[BalanceAmount]), 0.00) AS OutstandingAmount
    FROM [dbo].[Routes] r
    LEFT JOIN [dbo].[Customers] c ON r.[RouteId] = c.[RouteId]
    LEFT JOIN [dbo].[MonthlyBills] b ON c.[CustomerId] = b.[CustomerId] 
                                    AND b.[BillingMonth] = @Month 
                                    AND b.[BillingYear] = @Year 
                                    AND b.[IsPublished] = 1
    GROUP BY r.[RouteId], r.[RouteName]
    ORDER BY r.[RouteName] ASC;
END;
GO

-- ====================================================================================
-- 8. Least-Privilege Security Provisioning & Ownership Chaining
-- ====================================================================================
PRINT 'Provisioning security permissions for application role db_dms_app...';

-- 8.1 Revoke legacy permissions
REVOKE INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocations] FROM [db_dms_app];
REVOKE EXECUTE ON [dbo].[usp_AllocatePayment] FROM [db_dms_app];

-- 8.2 Grant SELECT ONLY on PaymentAllocationEvents and PaymentAuditLogs
GRANT SELECT ON [dbo].[PaymentAllocationEvents] TO [db_dms_app];
GRANT SELECT ON [dbo].[PaymentAuditLogs] TO [db_dms_app];
GRANT SELECT ON [dbo].[Payments] TO [db_dms_app];
GRANT SELECT ON [dbo].[MonthlyBills] TO [db_dms_app];
GRANT SELECT ON [dbo].[MonthlyBillItems] TO [db_dms_app];
GRANT SELECT ON [dbo].[MonthlyBillRevisions] TO [db_dms_app];
GRANT SELECT ON [dbo].[Routes] TO [db_dms_app];

-- 8.3 Revoke direct writes on billing entities (forcing stored procedure usage)
REVOKE INSERT, UPDATE, DELETE ON [dbo].[MonthlyBills] FROM [db_dms_app];
REVOKE INSERT, UPDATE, DELETE ON [dbo].[MonthlyBillItems] FROM [db_dms_app];
REVOKE INSERT, UPDATE, DELETE ON [dbo].[MonthlyBillRevisions] FROM [db_dms_app];
REVOKE INSERT, UPDATE, DELETE ON [dbo].[Payments] FROM [db_dms_app];

-- 8.4 Explicit DENY to guarantee direct writes are blocked across all roles
DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocationEvents] TO [db_dms_app];
DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocationEvents] TO [dms_app_user];

DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAuditLogs] TO [db_dms_app];
DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAuditLogs] TO [dms_app_user];

-- 8.4 Grant EXECUTE exclusively on approved billing & payment stored procedures
GRANT EXECUTE ON [dbo].[usp_Payment_RecordAndAllocateSingle] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Payment_AllocateAdvanceCreditSingle] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_ReviseBill] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Payment_Void] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_GenerateCustomerBill] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_GenerateBulk] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_PublishBill] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_BulkPublish] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_GetBillsContinuous] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_MonthlyBilling_GetBillDetails] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Payment_GetCustomerOutstandingBills] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Payment_GetHistory] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_BillingReports_GetMonthlySummary] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT '08_BillingAndPaymentsManagement migration completed successfully!';
PRINT '====================================================================================';
GO
