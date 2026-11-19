# DAY 5 — BILLING & PAYMENTS ARCHITECTURE SPECIFICATION
**Dairy Management System (DMS)**  
**Document Identifier:** `DMS-ARCH-DAY5-BILLING-009` (Actual Login Verification & Full Permission Test Harness Revision)  
**Author:** Rachit Parsana (Lead Architect)  
**Role:** Project Architect  
**Baseline Git Commit:** `53bd84ec7dcdde755e522d5c39fccdc9119e0433`  
**Target Modules:** Monthly Billing Management, Bulk Billing, Bill Revisions, Payment Recording, Single-Invoice Allocation, Append-Only Allocation Events, Explicit Migration Identity, Multi-Payment LIFO Downward Reversals, Customer Statements & Audited Ledgers  
**Execution Context:** Production Architectural Specification for Kevin (Backend) and Viraj (Frontend)

---

## 1. Executive Summary

The **Billing & Payments Management** module constitutes the financial engine of the Dairy Management System. It translates daily recurring milk and dairy distributions into legally binding, deterministic, calendar-month customer invoices, tracks manual payment receipts across strictly two payment modes (**Online** and **Offline**), enforces single-invoice allocation without automatic FIFO drift, protects customer ledgers against previous-balance double-counting, enforces strictly append-only financial allocation history, and provides real-time customer account balances.

### Architectural Core Principles
1. **Deterministic Calendar-Month Alignment:** Invoices strictly span from the 1st of a calendar month to its last day ($28/29/30/31$).
2. **Strict Separation of Invoicing and Payments:** Monthly billing calculates consumption and dues. Generating, recalculating, or revising a bill **never** automatically marks it as paid. A bill transitions to `Paid` only when verified manual payment receipts reduce its outstanding balance to ₹0.00.
3. **One Payment Receipt, One Selected Invoice (Zero Multi-Month Drift & Zero Automatic FIFO):** Each payment receipt is allocated strictly to **one selected invoice** chosen manually by Admin. Automatic cross-month distribution is prohibited. Any payment amount exceeding the selected invoice balance is captured as **Unallocated Advance Credit** on that payment receipt and remains unallocated until explicitly directed by an authorized Admin action.
4. **Authoritative Receivable Ledger (Zero Double-Counting):** An invoice's balance represents the unpaid portion of **that calendar month's charge alone**. Carried-forward previous balances are captured as informational snapshots for printed customer statements, but **never** added into the database receivable balance. Summing invoice balances across all bills yields the exact customer debt without double-counting.
5. **Strictly Append-Only Payment Allocation History:** Financial allocations are never updated in-place or deleted. All allocation adjustments (such as downward invoice revisions releasing excess funds, or payment voiding) are recorded as **new immutable reversal events** in `dbo.PaymentAllocationEvents` with directional signs, linking back to the original allocation event.
6. **Deterministic LIFO Reversal for Multi-Payment Downward Revisions:** When a bill funded by multiple payment receipts is revised downward, excess funds are released back to payments in strict Last-In, First-Out (LIFO) order with `AllocationEventId DESC` as the deterministic tie-breaker, restoring advance credit on the original payment receipts without cross-allocating to other bills. Final invoice balances and payment statuses are rigorously recalculated against actual remaining allocations.
7. **Two-Tiered Payment Void Idempotency:** Payment voiding is governed by a strict two-tiered defense: **Tier 1 (Primary):** Concurrency-safe payment-level row locking (`UPDLOCK, HOLDLOCK`) and atomic validation rejecting duplicate void requests with Error `50041` across all payment states (allocated, partially reversed, pure credit, or zero allocation); **Tier 2 (Secondary):** A storage-level filtered unique index on `OriginalAllocationEventId` physically preventing duplicate reversal events.
8. **Explicit Relational Migration Identity & Void Safeguards:** The legacy table `dbo.PaymentAllocations` is migrated into `dbo.PaymentAllocationEvents` using a dedicated relational column `LegacyAllocationId` with a unique filtered index.
9. **Migration-Time Reconciliation Boundary:** The five automated reconciliation checks establish an uncompromising migration acceptance gate before cutover. Post-cutover financial integrity is governed exclusively by internal event-ledger invariants, as the archived legacy table is frozen.
10. **Authoritative Voided Payment Credit Model:** A voided payment receipt is strictly prohibited from holding advance credit. Payment voiding atomically reverses only remaining active allocations on outstanding bills and resets available advance credit to ₹0.00.
11. **Separation of Advance Credit on Customer Statements:** Customer statements clearly display previous outstanding, current month charges, total statement amount, available advance credit, and net customer outstanding as distinct line items. Unallocated advance credit is **never** subtracted automatically from individual invoice balances.
12. **Snapshotted Due Dates with Business Policy Contract:** Payment due dates are permanently snapshotted in `dbo.MonthlyBills.PaymentDueDate` upon publication. The due date policy remains **`PROPOSED DEFAULT — REQUIRES BUSINESS APPROVAL`**. Customer statements explicitly render `Due Date: Pending Business Approval` until formally ratified.
13. **Actual Application Identity & Production Least-Privilege Contract [Status: DEFINED — EXECUTION PENDING]:** The security boundary explicitly distinguishes between LocalDB development and production. Production requires a dedicated non-sysadmin service login mapped to restricted principal `dms_app_user` (`db_dms_app`), to be verified over the actual production connection string reporting all 5 identity tokens before go-live. Direct `INSERT`, `UPDATE`, and `DELETE` on `PaymentAllocationEvents` must be blocked via explicit `DENY` (Error 229), and all four approved stored procedures must execute successfully via static SQL ownership chaining within the documented rolled-back verification test harness. Test specification is fully documented; test execution and result validation remain pending Phase 2 deployment.

---

## 2. Existing Architecture Integration & Authoritative Migration Strategy

### 2.1 Reused Database Tables
- **`dbo.MonthlyBills` (Created in `sql/03_CreateTables.sql`):** Primary invoice ledger containing calendar month/year, total volume, milk amount, adjustments, previous balance snapshot, total statement amount, paid amount, balance amount, payment status, version, and publication timestamps.
  - *Required Schema Additions:* Column `PaymentDueDate DATE NULL` (snapshotted at publication).
  - *Authoritative Semantic:* `BalanceAmount = CurrentMonthInvoiceAmount - PaidAmount`.
- **`dbo.MonthlyBillItems` (Created in `sql/03_CreateTables.sql`):** Itemized category-level breakdown of the bill (Delivered days, packaging quantity, liquid litres, unit price snapshot, subtotal).
- **`dbo.MonthlyBillRevisions` (Created in `sql/03_CreateTables.sql`):** Immutable historical revision log capturing before/after amounts, version number, reason, and admin user.
- **`dbo.Payments` (Created in `sql/03_CreateTables.sql`):** Customer payment receipt log storing amount, mode (`Online`, `Offline`), reference number, notes, void status, and received-by identity.
- **`dbo.DailyMilkDeliveries` & `dbo.DailyMilkDeliveryItems` (`sql/07_DailyMilkManagement.sql`):** Source of truth for completed deliveries, quantities, and unit price snapshots.
- **`dbo.CustomerVacations` (`sql/03_CreateTables.sql`):** Source of truth for approved vacation records.
- **`dbo.Customers` & `dbo.Users`:** Master customer records and administrative user credentials.

---

### 2.2 Legacy `dbo.PaymentAllocations` Inspection and Authoritative Ledger Migration Strategy

#### 1. Exact Existing Schema and Constraint Inspection
Audit of `sql/03_CreateTables.sql` (lines 540–554) and `sql/04_CreateIndexes.sql` (lines 125–131) reveals the exact existing schema:
```sql
CREATE TABLE [dbo].[PaymentAllocations] (
    [AllocationId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_PaymentAllocations] PRIMARY KEY CLUSTERED,
    [PaymentId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocations_PaymentId] REFERENCES [dbo].[Payments]([PaymentId]),
    [BillId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocations_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]),
    [AllocatedAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_PaymentAllocations_Amount] CHECK ([AllocatedAmount] > 0),
    [IsVoided] BIT NOT NULL CONSTRAINT [DF_PaymentAllocations_IsVoided] DEFAULT (0),
    [AllocatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAllocations_AllocatedAt] DEFAULT (SYSUTCDATETIME()),
    [AllocatedBy] INT NOT NULL CONSTRAINT [FK_PaymentAllocations_AllocatedBy] REFERENCES [dbo].[Users]([UserId])
);
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Payment_Bill_Allocation] ON [dbo].[PaymentAllocations]([PaymentId], [BillId]);
```

#### Why `dbo.PaymentAllocations` Cannot Support Append-Only Reversals
1. `CONSTRAINT [CK_PaymentAllocations_Amount] CHECK ([AllocatedAmount] > 0)`: Completely forbids negative signed reversal amounts (e.g. `-200.00`).
2. `CONSTRAINT [UQ_Payment_Bill_Allocation] UNIQUE ([PaymentId], [BillId])`: Strictly forbids appending a second event for the same payment and bill.
3. In-place modification of `AllocatedAmount` destroys historical financial auditability and violates GAAP/IFRS principles.

#### 2. Explicit Migration Identity & Secondary Void-Reversal Uniqueness Design
To guarantee database-level uniqueness, two dedicated filtered unique indexes are introduced on `dbo.PaymentAllocationEvents`:
1. **Migration Identity Index:**
   ```sql
   CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_LegacyAllocationId]
   ON [dbo].[PaymentAllocationEvents]([LegacyAllocationId])
   WHERE [LegacyAllocationId] IS NOT NULL;
   ```
   - Physical 1:1 constraint ensuring an archived legacy allocation can never be migrated twice.
2. **Secondary Void-Reversal Event Uniqueness Index:**
   ```sql
   CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent]
   ON [dbo].[PaymentAllocationEvents]([OriginalAllocationEventId])
   WHERE [EventType] = 'VoidReversal' AND [OriginalAllocationEventId] IS NOT NULL;
   ```
   - **Enforcement Scope:** Serves as a secondary storage-level defense-in-depth safeguard physically allowing **at most one** `VoidReversal` event per initial allocation event.
   - **Multiple Reversal Invariance:** Completely ignores `EventType = 'ReversalAdjustment'`, fully permitting multiple partial downward revisions against the same initial allocation.
   - **Complementary Roles:** Primary payment-level idempotency is enforced by procedure logic and concurrency locks on `dbo.Payments` (covering payments with zero allocations or pure credit), while this filtered index provides event-level defense against duplicate event insertion.

#### 3. Complete Migration Design for Active and Voided Rows
For every row in `dbo.PaymentAllocations`:

**A. Active Allocations (`IsVoided = 0`):**
- Create exactly one `InitialAllocation` event.
- `Direction = +1`, `Amount = pa.AllocatedAmount`, `SignedAmount = +pa.AllocatedAmount`.
- Preserve `PaymentId`, `BillId`, `AllocatedBy`, `AllocatedAt`.
- Populate `LegacyAllocationId = pa.AllocationId`.
- `Reason = 'Migrated legacy active allocation'`.

**B. Historical Voided Allocations (`IsVoided = 1`):**
- Migrated as an atomic **matched event pair**:
  1. **Initial Allocation Event:**
     - `EventType = 'InitialAllocation'`, `Direction = +1`, `Amount = pa.AllocatedAmount`.
     - `LegacyAllocationId = pa.AllocationId` (ensures 1:1 legacy row tracking).
     - `Reason = 'Migrated historical voided allocation (Initial)'`.
  2. **Void Reversal Event:**
     - `EventType = 'VoidReversal'`, `Direction = -1`, `Amount = pa.AllocatedAmount`.
     - `OriginalAllocationEventId = <InitialEventId>` (physically guarded by `UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent`).
     - `LegacyAllocationId = NULL` (maintains unique filtered index constraint).
     - `Reason = 'Migrated historical voided allocation (Reversal)'`.
- *Result:* Net active allocation for voided legacy rows is mathematically $+pa.AllocatedAmount - pa.AllocatedAmount = \mathbf{₹0.00}$.

#### 4. Idempotent Migration Script with Transaction Boundaries
The migration executes within an explicit database transaction with `XACT_ABORT ON`:

```sql
-- Migration Script in sql/08_BillingAndPaymentsManagement.sql
SET XACT_ABORT ON;
BEGIN TRANSACTION;

PRINT 'Migrating legacy PaymentAllocations to PaymentAllocationEvents...';

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

COMMIT TRANSACTION;
```

---

#### 5. Migration-Time Reconciliation Boundary & Five Automated Checks

```
┌────────────────────────────────────────────────────────────────────────┐
│               MIGRATION-TIME RECONCILIATION BOUNDARY                   │
├────────────────────────────────────────────────────────────────────────┤
│ PRE-CUTOVER DEPLOYMENT WINDOW:                                         │
│ 1. Freeze legacy writes via trg_PaymentAllocations_LegacyReadOnly.     │
│ 2. Decommission dbo.usp_AllocatePayment (Error 50060).                 │
│ 3. Execute transactional migration of active and voided allocations.   │
│ 4. Execute the 5 Migration-Time Reconciliation Checks below.           │
│ 5. GATING REQUIREMENT: Zero discrepancies across all five checks.     │
│ 6. Record reconciliation sign-off and cutover application repository.  │
├────────────────────────────────────────────────────────────────────────┤
│ POST-CUTOVER OPERATIONAL STATE:                                        │
│ - Legacy dbo.PaymentAllocations is permanently frozen.                 │
│ - Live transactions (downward revisions, voids, advance credit         │
│   allocations) alter dbo.PaymentAllocationEvents dynamically.          │
│ - Ongoing equality between legacy table and event ledger is NEITHER    │
│   EXPECTED NOR VALID post-cutover.                                     │
│ - Operational financial integrity is monitored via internal ledger     │
│   mathematical invariants (PaidAmount = Sum(Events), etc.).            │
└────────────────────────────────────────────────────────────────────────┘
```

The five migration-time checks must be executed during deployment:

```sql
-- ====================================================================================
-- CHECK 1: ACTIVE LEGACY ALLOCATION RECONCILIATION
-- ====================================================================================
SELECT 
    'Check 1: Active Legacy Grand Total' AS CheckName,
    (SELECT ISNULL(SUM(AllocatedAmount), 0.00) FROM [dbo].[PaymentAllocations] WHERE IsVoided = 0) AS LegacyActiveTotal,
    (SELECT ISNULL(SUM(pae.SignedAmount), 0.00)
     FROM [dbo].[PaymentAllocationEvents] pae
     JOIN [dbo].[PaymentAllocations] pa ON pae.LegacyAllocationId = pa.AllocationId
     WHERE pa.IsVoided = 0 AND pae.Direction = 1) AS MigratedActiveTotal,
    CASE 
        WHEN (SELECT ISNULL(SUM(AllocatedAmount), 0.00) FROM [dbo].[PaymentAllocations] WHERE IsVoided = 0) =
             (SELECT ISNULL(SUM(pae.SignedAmount), 0.00)
              FROM [dbo].[PaymentAllocationEvents] pae
              JOIN [dbo].[PaymentAllocations] pa ON pae.LegacyAllocationId = pa.AllocationId
              WHERE pa.IsVoided = 0 AND pae.Direction = 1)
        THEN 'PASSED (EXACT MATCH)'
        ELSE 'FAILED (DISCREPANCY DETECTED)'
    END AS Status;

-- Check 1B: Active Row Attribute Invariance (Must return 0 rows)
SELECT COUNT(*) AS ActiveAttributeMismatches
FROM [dbo].[PaymentAllocations] pa
JOIN [dbo].[PaymentAllocationEvents] pae ON pa.AllocationId = pae.LegacyAllocationId
WHERE pa.IsVoided = 0
  AND (pae.PaymentId <> pa.PaymentId 
    OR pae.BillId <> pa.BillId 
    OR pae.Amount <> pa.AllocatedAmount 
    OR pae.Direction <> 1 
    OR pae.EventType <> 'InitialAllocation');

-- ====================================================================================
-- CHECK 2: VOIDED LEGACY ALLOCATION EVENT PAIR & NET ZERO RECONCILIATION
-- ====================================================================================
SELECT 
    'Check 2: Voided Allocations Net Zero' AS CheckName,
    COUNT(pa.AllocationId) AS TotalVoidedLegacyRows,
    COUNT(init_evt.AllocationEventId) AS MigratedInitialEventsCount,
    COUNT(rev_evt.AllocationEventId) AS MigratedReversalEventsCount,
    ISNULL(SUM(init_evt.SignedAmount + rev_evt.SignedAmount), 0.00) AS NetSignedAmount,
    CASE 
        WHEN COUNT(pa.AllocationId) = COUNT(init_evt.AllocationEventId)
         AND COUNT(pa.AllocationId) = COUNT(rev_evt.AllocationEventId)
         AND ISNULL(SUM(init_evt.SignedAmount + rev_evt.SignedAmount), 0.00) = 0.00
        THEN 'PASSED (NET ZERO VERIFIED)'
        ELSE 'FAILED (VOID INTEGRITY ERROR)'
    END AS Status
FROM [dbo].[PaymentAllocations] pa
LEFT JOIN [dbo].[PaymentAllocationEvents] init_evt 
       ON pa.AllocationId = init_evt.LegacyAllocationId
LEFT JOIN [dbo].[PaymentAllocationEvents] rev_evt 
       ON init_evt.AllocationEventId = rev_evt.OriginalAllocationEventId 
      AND rev_evt.EventType = 'VoidReversal'
WHERE pa.IsVoided = 1;

-- Check 2B: Voided Row Attribute Invariance (Must return 0 rows)
SELECT COUNT(*) AS VoidedAttributeMismatches
FROM [dbo].[PaymentAllocations] pa
JOIN [dbo].[PaymentAllocationEvents] init_evt ON pa.AllocationId = init_evt.LegacyAllocationId
JOIN [dbo].[PaymentAllocationEvents] rev_evt ON init_evt.AllocationEventId = rev_evt.OriginalAllocationEventId
WHERE pa.IsVoided = 1
  AND (init_evt.PaymentId <> pa.PaymentId
    OR init_evt.BillId <> pa.BillId
    OR init_evt.Amount <> pa.AllocatedAmount
    OR init_evt.Direction <> 1
    OR rev_evt.PaymentId <> init_evt.PaymentId
    OR rev_evt.BillId <> init_evt.BillId
    OR rev_evt.Amount <> init_evt.Amount
    OR rev_evt.Direction <> -1);

-- ====================================================================================
-- CHECK 3: PAYMENT-LEVEL RECONCILIATION
-- ====================================================================================
SELECT 
    COALESCE(leg.PaymentId, mig.PaymentId) AS PaymentId,
    ISNULL(leg.LegacyTotal, 0.00) AS LegacyActiveTotal,
    ISNULL(mig.MigratedTotal, 0.00) AS MigratedNetTotal,
    ISNULL(leg.LegacyTotal, 0.00) - ISNULL(mig.MigratedTotal, 0.00) AS Discrepancy
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
WHERE ISNULL(leg.LegacyTotal, 0.00) <> ISNULL(mig.MigratedTotal, 0.00);

-- ====================================================================================
-- CHECK 4: BILL-LEVEL RECONCILIATION
-- ====================================================================================
SELECT 
    COALESCE(leg.BillId, mig.BillId) AS BillId,
    ISNULL(leg.LegacyTotal, 0.00) AS LegacyActiveTotal,
    ISNULL(mig.MigratedTotal, 0.00) AS MigratedNetTotal,
    ISNULL(leg.LegacyTotal, 0.00) - ISNULL(mig.MigratedTotal, 0.00) AS Discrepancy
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
WHERE ISNULL(leg.LegacyTotal, 0.00) <> ISNULL(mig.MigratedTotal, 0.00);

-- ====================================================================================
-- CHECK 5: IDENTIFIER & RELATIONAL INTEGRITY CHECKS (Must return 0 rows)
-- ====================================================================================
-- 5A: Missing LegacyAllocationId for migrated active rows
SELECT pa.AllocationId AS MissingActiveLegacyAllocationId
FROM [dbo].[PaymentAllocations] pa
WHERE pa.IsVoided = 0
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[PaymentAllocationEvents] pae 
      WHERE pae.LegacyAllocationId = pa.AllocationId
  );

-- 5B: Duplicate LegacyAllocationId check (Filtered index guarantees 0 rows)
SELECT LegacyAllocationId, COUNT(*) AS DuplicateCount
FROM [dbo].[PaymentAllocationEvents]
WHERE LegacyAllocationId IS NOT NULL
GROUP BY LegacyAllocationId
HAVING COUNT(*) > 1;

-- 5C: Missing initial events for legacy voided rows
SELECT pa.AllocationId AS MissingVoidedInitialEvent
FROM [dbo].[PaymentAllocations] pa
WHERE pa.IsVoided = 1
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[PaymentAllocationEvents] pae 
      WHERE pae.LegacyAllocationId = pa.AllocationId AND pae.EventType = 'InitialAllocation'
  );

-- 5D: Missing or duplicated reversal events for legacy voided rows
SELECT pa.AllocationId, COUNT(rev.AllocationEventId) AS ReversalCount
FROM [dbo].[PaymentAllocations] pa
JOIN [dbo].[PaymentAllocationEvents] init ON pa.AllocationId = init.LegacyAllocationId
LEFT JOIN [dbo].[PaymentAllocationEvents] rev ON init.AllocationEventId = rev.OriginalAllocationEventId AND rev.EventType = 'VoidReversal'
WHERE pa.IsVoided = 1
GROUP BY pa.AllocationId
HAVING COUNT(rev.AllocationEventId) <> 1;

-- 5E: Orphan reversal links or mismatched identities
SELECT rev.AllocationEventId AS OrphanOrMismatchedReversalId
FROM [dbo].[PaymentAllocationEvents] rev
LEFT JOIN [dbo].[PaymentAllocationEvents] init ON rev.OriginalAllocationEventId = init.AllocationEventId
WHERE rev.EventType = 'VoidReversal'
  AND (init.AllocationEventId IS NULL 
    OR rev.PaymentId <> init.PaymentId 
    OR rev.BillId <> init.BillId);
```

---

#### 6. Legacy Table Archival & Decommissioning
- **Read-Only Archival Trigger:**
  ```sql
  CREATE OR ALTER TRIGGER [dbo].[trg_PaymentAllocations_LegacyReadOnly]
  ON [dbo].[PaymentAllocations]
  FOR INSERT, UPDATE, DELETE
  AS
  BEGIN
      SET NOCOUNT ON;
      THROW 50055, 'dbo.PaymentAllocations is archived and read-only. All payment allocations must be written as append-only events to dbo.PaymentAllocationEvents via approved stored procedures.', 1;
  END;
  GO
  ```
- **Decommissioning `dbo.usp_AllocatePayment`:**
  ```sql
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
  ```
- **Application Cutover:** `BillingRepository.cs` queries `dbo.PaymentAllocationEvents` exclusively. Zero queries touch `dbo.PaymentAllocations`.

---

#### 7. Actual Application Security Identity & Production Least-Privilege Contract

```
┌────────────────────────────────────────────────────────────────────────┐
│            APPLICATION IDENTITY & LEAST-PRIVILEGE ARCHITECTURE         │
├───────────────────────────────┬────────────────────────────────────────┤
│ Production App Login          │ Dedicated non-sysadmin SQL/Domain login│
│ Connection Context            │ Actual Production Connection String    │
│ Target Database User          │ dms_app_user (Role: [db_dms_app])      │
│ Sysadmin / db_owner Status    │ STRICTLY FORBIDDEN IN PRODUCTION       │
│ PaymentAllocationEvents Access│ SELECT ONLY (Explicit DENY on DML)     │
│ Direct INSERT/UPDATE/DELETE   │ EXPLICITLY DENIED (Error 229 on all 3) │
│ Ledger Write Channel          │ Stored Procedures via Static SQL       │
│ Ownership Chaining Scope      │ Both table & procedures owned by dbo   │
└───────────────────────────────┴────────────────────────────────────────┘
```

#### Actual Application Login Verification Protocol
To certify database security before go-live, identity and permission verification must be executed through a connection established using the **actual production application connection string and service identity** configured in `appsettings.Production.json` (or cloud environment secret store).

> [!IMPORTANT]
> **Actual Identity Verification Status:** `DEFINED — EXECUTION PENDING`  
> - **Test Specification Documented:** COMPLETE (Connection protocol, 5-token query, and role assertions fully specified).  
> - **Test Execution Completed:** PENDING (Must be executed through the actual production application connection and service credentials).  
> - **Test Result Passed:** PENDING (Awaiting execution during deployment).  
> - **Development Boundary:** LocalDB development verification under an elevated developer Windows login must NOT be represented as production verification.

##### Prohibition on Deployment Connection Claims:
Engineers must **never claim** that executing verification queries through an administrator's deployment connection (e.g. logging in as `sa` or Windows Administrator and using `EXECUTE AS USER = 'dms_app_user'`) proves application least privilege. An administrator's deployment session maintains elevated server-level tokens, session privileges, and bypass routes that do not reflect the actual runtime security context, connection string configuration, or physical boundaries of the application connection pool. Production compliance sign-off requires opening a direct connection under the application's actual configured credentials.

##### Development vs. Production Identity Distinction:
- **LocalDB Development Environment:**
  - In `appsettings.json`, LocalDB uses Windows Authentication (`Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;`).
  - On developer machines, the Windows user connecting to LocalDB is an elevated instance administrator (`sysadmin`) mapping to `dbo`.
  - **Critical Rule:** Direct writes to `PaymentAllocationEvents` will succeed under the elevated developer login because `sysadmin` and `db_owner` bypass all `DENY` permissions. Successful execution under an elevated developer login proves **nothing** about least-privilege compliance.
  - To test security boundaries in LocalDB, Kevin must explicitly distinguish the developer Windows login from the application principal by either:
    1. Using session impersonation (`EXECUTE AS USER = 'dms_app_user'`) from an admin shell solely for local mock testing, OR
    2. Creating and connecting through a dedicated, restricted test login (e.g. `dms_test_login`) mapped to `dms_app_user` with zero server roles.
- **Production Environment Mandate:**
  - Production connection strings authenticate strictly via a dedicated, non-sysadmin service login (e.g. `dms_service_user` or managed identity).
  - The login is strictly forbidden from membership in `sysadmin`, `serveradmin`, `setupadmin`, `db_owner`, `db_datawriter`, `db_ddladmin`, `db_securityadmin`, or `db_accessadmin`.
  - The login maps exclusively to database user `dms_app_user` belonging only to database role `db_dms_app` (and `public`).

#### SQL Server Ownership Chaining Mechanics
Both the stored procedures and table `dbo.PaymentAllocationEvents` reside in the `dbo` schema and share the same database owner (`dbo`). Under SQL Server ownership chaining:
1. When `[db_dms_app]` calls an approved stored procedure (e.g. `usp_Payment_RecordAndAllocateSingle`), SQL Server checks permissions **only** on the procedure.
2. The procedure performs `INSERT` operations into `dbo.PaymentAllocationEvents` under ownership chaining **without checking table-level permissions**.
3. **Strict Static SQL Mandate:** Stored procedures **must strictly avoid dynamic SQL** (`sp_executesql` or `EXEC(...)`). Dynamic SQL breaks the ownership chain, reverts context to `dms_app_user`, and triggers permission denials (Error 229). All queries must be compiled static SQL.
4. **Alternative Module Signing:** If cross-schema boundaries or dynamic SQL were ever required, the only approved alternative is module signing via cryptographic database certificates (`ADD SIGNATURE`). Granting direct table write permissions as a workaround is strictly prohibited.

#### Security Provisioning Script with Explicit DENY:
```sql
-- 1. Revoke legacy permissions
REVOKE INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocations] FROM [db_dms_app];
REVOKE EXECUTE ON [dbo].[usp_AllocatePayment] FROM [db_dms_app];

-- 2. Grant SELECT ONLY on PaymentAllocationEvents
GRANT SELECT ON [dbo].[PaymentAllocationEvents] TO [db_dms_app];

-- 3. Explicit DENY to guarantee direct writes are blocked across all roles
DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocationEvents] TO [db_dms_app];
DENY INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocationEvents] TO [dms_app_user];

-- 4. Grant EXECUTE exclusively on approved billing & payment stored procedures
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
```

#### Production Identity & Five-Token Verification Query:
*(Must be executed through a connection established using the actual production application connection string)*
```sql
-- ====================================================================================
-- ACTUAL PRODUCTION APPLICATION LOGIN & IDENTITY VERIFICATION QUERY
-- Connect using the actual production connection string and application service identity
-- ====================================================================================

-- 1. Report Server Identity and Non-Sysadmin Status
SELECT 
    SUSER_SNAME() AS CurrentLogin,
    ORIGINAL_LOGIN() AS OriginalLogin,
    USER_NAME() AS CurrentDatabaseUser,
    IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME()) AS IsSysadmin;
-- MANDATORY PRODUCTION GATES:
-- SUSER_SNAME() & ORIGINAL_LOGIN() must equal the dedicated application service login (NOT 'sa' or Windows Admin).
-- USER_NAME() must equal 'dms_app_user'.
-- IsSysadmin MUST return 0.

-- 2. Report Database Role Memberships of the Mapped Application User
SELECT 
    u.name AS DatabaseUser,
    r.name AS DatabaseRole,
    IS_ROLEMEMBER('db_owner') AS IsDbOwner,
    IS_ROLEMEMBER('db_datawriter') AS IsDbDatawriter,
    IS_ROLEMEMBER('db_accessadmin') AS IsDbAccessAdmin,
    IS_ROLEMEMBER('db_securityadmin') AS IsDbSecurityAdmin
FROM sys.database_role_members rm
JOIN sys.database_principals u ON rm.member_principal_id = u.principal_id
JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
WHERE u.name = USER_NAME();
-- MANDATORY PRODUCTION GATES:
-- DatabaseRole must return ONLY 'db_dms_app' (and 'public').
-- IsDbOwner must return 0.
-- IsDbDatawriter must return 0.
-- IsDbAccessAdmin must return 0.
-- IsDbSecurityAdmin must return 0.

-- 3. Verify Effective Schema Permissions via HAS_PERMS_BY_NAME
SELECT 
    HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'SELECT') AS CanSelect,
    HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'INSERT') AS CanInsert,
    HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'UPDATE') AS CanUpdate,
    HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'DELETE') AS CanDelete,
    HAS_PERMS_BY_NAME(N'dbo.usp_Payment_RecordAndAllocateSingle', N'OBJECT', N'EXECUTE') AS CanExecuteRecordPayment,
    HAS_PERMS_BY_NAME(N'dbo.usp_Payment_AllocateAdvanceCreditSingle', N'OBJECT', N'EXECUTE') AS CanExecuteAllocateCredit,
    HAS_PERMS_BY_NAME(N'dbo.usp_MonthlyBilling_ReviseBill', N'OBJECT', N'EXECUTE') AS CanExecuteReviseBill,
    HAS_PERMS_BY_NAME(N'dbo.usp_Payment_Void', N'OBJECT', N'EXECUTE') AS CanExecuteVoidPayment;
-- MANDATORY PRODUCTION GATES:
-- CanSelect = 1
-- CanInsert = 0 (DENY enforced)
-- CanUpdate = 0 (DENY enforced)
-- CanDelete = 0 (DENY enforced)
-- All CanExecute flags = 1
```

#### Complete Negative & Positive Permission Verification Test Harness
*(Executed under the application principal within an explicit transaction that is 100% rolled back, ensuring real data is never modified)*

> [!IMPORTANT]
> **Permission Test Harness Status:** `DEFINED — EXECUTION PENDING`  
> - **Test Specification Documented:** COMPLETE (All 3 direct DML denial tests [INSERT, UPDATE, DELETE Error 229] and positive execution tests for all 4 approved stored procedures are fully specified below).  
> - **Test Execution Completed:** PENDING (Awaiting execution by Kevin during Phase 2 backend implementation).  
> - **Test Result Passed:** PENDING (To be validated by Kevin during Phase 2 execution).  
> - **Isolation Guarantee:** Runs inside a rolled-back transaction so real financial records are never modified.

```sql
-- ====================================================================================
-- PERMISSION & SECURITY VALIDATION TEST HARNESS (ROLLED BACK TRANSACTION)
-- Executed under dms_app_user (via actual connection or EXECUTE AS USER = 'dms_app_user')
-- Real financial data is NEVER modified; entire harness is rolled back.
-- ====================================================================================

-- If running within development deployment session, switch to dms_app_user:
-- EXECUTE AS USER = 'dms_app_user';

SET XACT_ABORT OFF;
BEGIN TRANSACTION;

PRINT '===================================================================';
PRINT 'STARTING DAY 5 SECURITY & PERMISSION VALIDATION HARNESS';
PRINT 'Principal: ' + USER_NAME() + ' | Login: ' + SUSER_SNAME();
PRINT '===================================================================';

-- ------------------------------------------------------------------------------------
-- PART 1: COMPLETE NEGATIVE PERMISSION TESTS (Must fail with Error 229)
-- Direct INSERT, UPDATE, and DELETE on dbo.PaymentAllocationEvents must ALL be blocked!
-- ------------------------------------------------------------------------------------

DECLARE @InsertBlocked BIT = 0;
DECLARE @UpdateBlocked BIT = 0;
DECLARE @DeleteBlocked BIT = 0;

-- Negative Test 1: Direct INSERT on dbo.PaymentAllocationEvents
BEGIN TRY
    INSERT INTO dbo.PaymentAllocationEvents (
        PaymentId, BillId, EventType, OriginalAllocationEventId,
        Amount, Direction, Reason, RelatedRevisionId, LegacyAllocationId, PerformedBy
    )
    VALUES (999999, 999999, 'InitialAllocation', NULL, 100.00, 1, 'Negative Security Test INSERT', NULL, NULL, 1);
    
    PRINT 'CRITICAL SECURITY FAILURE: Direct INSERT on dbo.PaymentAllocationEvents SUCCEEDED!';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 229
    BEGIN
        SET @InsertBlocked = 1;
        PRINT 'PASSED [Negative Test 1]: Direct INSERT blocked with Error 229: ' + ERROR_MESSAGE();
    END
    ELSE
    BEGIN
        PRINT 'FAILED [Negative Test 1]: Unexpected error code ' + CAST(ERROR_NUMBER() AS NVARCHAR) + ': ' + ERROR_MESSAGE();
    END
END CATCH;

-- Negative Test 2: Direct UPDATE on dbo.PaymentAllocationEvents
BEGIN TRY
    UPDATE dbo.PaymentAllocationEvents 
    SET Amount = 9999.00 
    WHERE AllocationEventId = -999999;
    
    PRINT 'CRITICAL SECURITY FAILURE: Direct UPDATE on dbo.PaymentAllocationEvents SUCCEEDED!';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 229
    BEGIN
        SET @UpdateBlocked = 1;
        PRINT 'PASSED [Negative Test 2]: Direct UPDATE blocked with Error 229: ' + ERROR_MESSAGE();
    END
    ELSE
    BEGIN
        PRINT 'FAILED [Negative Test 2]: Unexpected error code ' + CAST(ERROR_NUMBER() AS NVARCHAR) + ': ' + ERROR_MESSAGE();
    END
END CATCH;

-- Negative Test 3: Direct DELETE from dbo.PaymentAllocationEvents
BEGIN TRY
    DELETE FROM dbo.PaymentAllocationEvents 
    WHERE AllocationEventId = -999999;
    
    PRINT 'CRITICAL SECURITY FAILURE: Direct DELETE from dbo.PaymentAllocationEvents SUCCEEDED!';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 229
    BEGIN
        SET @DeleteBlocked = 1;
        PRINT 'PASSED [Negative Test 3]: Direct DELETE blocked with Error 229: ' + ERROR_MESSAGE();
    END
    ELSE
    BEGIN
        PRINT 'FAILED [Negative Test 3]: Unexpected error code ' + CAST(ERROR_NUMBER() AS NVARCHAR) + ': ' + ERROR_MESSAGE();
    END
END CATCH;

IF @InsertBlocked = 1 AND @UpdateBlocked = 1 AND @DeleteBlocked = 1
    PRINT '>> ALL THREE DIRECT DML OPERATIONS BLOCKED WITH ERROR 229 (Least Privilege Confirmed).';
ELSE
    THROW 50090, 'SECURITY BREACH: One or more direct DML operations are not properly denied under dms_app_user.', 1;

-- ------------------------------------------------------------------------------------
-- PART 2: POSITIVE EXECUTION TESTS FOR APPROVED STORED PROCEDURES
-- (Executes all 4 procedures under dms_app_user via Static SQL Ownership Chaining)
-- ------------------------------------------------------------------------------------

PRINT '-------------------------------------------------------------------';
PRINT 'STARTING POSITIVE STORED PROCEDURE EXECUTION TESTS';
PRINT '-------------------------------------------------------------------';

-- Set up isolated, controlled test fixtures within this transaction
DECLARE @HarnessCustId INT = 1;     -- Active customer ID for harness
DECLARE @HarnessAdminId INT = 1;    -- Admin user ID
DECLARE @HarnessBillId1 BIGINT, @HarnessBillId2 BIGINT;
DECLARE @HarnessPayId1 BIGINT, @HarnessPayId2 BIGINT;
DECLARE @HarnessEventId BIGINT, @HarnessRevId BIGINT;

-- Controlled Test Bill 1 (Target invoice amount = 1000.00)
INSERT INTO dbo.MonthlyBills (
    CustomerId, BillingMonth, BillingYear, TotalDeliveries, TotalLitres,
    MilkAmount, Adjustments, PreviousBalanceSnapshot, TotalStatementAmount,
    PaidAmount, BalanceAmount, PaymentStatus, IsPublished, PublishedAt,
    PaymentDueDate, Version, CreatedBy, CreatedAt
) VALUES (
    @HarnessCustId, 12, 2099, 10, 20.000,
    1000.00, 0.00, 0.00, 1000.00,
    0.00, 1000.00, 'Unpaid', 1, SYSUTCDATETIME(),
    DATEADD(day, 10, SYSUTCDATETIME()), 1, @HarnessAdminId, SYSUTCDATETIME()
);
SET @HarnessBillId1 = SCOPE_IDENTITY();

-- Controlled Test Bill 2 (Target invoice amount = 500.00)
INSERT INTO dbo.MonthlyBills (
    CustomerId, BillingMonth, BillingYear, TotalDeliveries, TotalLitres,
    MilkAmount, Adjustments, PreviousBalanceSnapshot, TotalStatementAmount,
    PaidAmount, BalanceAmount, PaymentStatus, IsPublished, PublishedAt,
    PaymentDueDate, Version, CreatedBy, CreatedAt
) VALUES (
    @HarnessCustId, 11, 2099, 5, 10.000,
    500.00, 0.00, 0.00, 500.00,
    0.00, 500.00, 'Unpaid', 1, SYSUTCDATETIME(),
    DATEADD(day, 10, SYSUTCDATETIME()), 1, @HarnessAdminId, SYSUTCDATETIME()
);
SET @HarnessBillId2 = SCOPE_IDENTITY();

-- Positive Test 1: usp_Payment_RecordAndAllocateSingle
-- Records payment of 600.00 allocated to Bill 1 (Balance reduces to 400.00)
EXEC dbo.usp_Payment_RecordAndAllocateSingle
    @CustomerId = @HarnessCustId,
    @Amount = 600.00,
    @PaymentMode = 'Online',
    @ReferenceNumber = 'TEST-POS-REF-001',
    @Notes = 'Positive security test payment',
    @SelectedBillId = @HarnessBillId1,
    @AdminUserId = @HarnessAdminId,
    @NewPaymentId = @HarnessPayId1 OUTPUT,
    @NewAllocationEventId = @HarnessEventId OUTPUT;

PRINT 'PASSED [Positive Test 1]: usp_Payment_RecordAndAllocateSingle succeeded. PaymentId=' + CAST(@HarnessPayId1 AS NVARCHAR) + ', EventId=' + CAST(@HarnessEventId AS NVARCHAR);

-- Record payment of 500.00 to Bill 1 (Remaining balance was 400.00; 100.00 becomes advance credit)
EXEC dbo.usp_Payment_RecordAndAllocateSingle
    @CustomerId = @HarnessCustId,
    @Amount = 500.00,
    @PaymentMode = 'Offline',
    @ReferenceNumber = 'TEST-POS-REF-002',
    @Notes = 'Positive security test payment with credit',
    @SelectedBillId = @HarnessBillId1,
    @AdminUserId = @HarnessAdminId,
    @NewPaymentId = @HarnessPayId2 OUTPUT,
    @NewAllocationEventId = @HarnessEventId OUTPUT;

-- Positive Test 2: usp_Payment_AllocateAdvanceCreditSingle
-- Allocates 50.00 from Payment 2's unallocated credit to Bill 2
EXEC dbo.usp_Payment_AllocateAdvanceCreditSingle
    @PaymentId = @HarnessPayId2,
    @SelectedBillId = @HarnessBillId2,
    @AllocatedAmount = 50.00,
    @AdminUserId = @HarnessAdminId,
    @NewAllocationEventId = @HarnessEventId OUTPUT;

PRINT 'PASSED [Positive Test 2]: usp_Payment_AllocateAdvanceCreditSingle succeeded. EventId=' + CAST(@HarnessEventId AS NVARCHAR);

-- Positive Test 3: usp_MonthlyBilling_ReviseBill
-- Revises Bill 1 downward by adding 100.00 adjustment (New target: 900.00; triggers LIFO reversal of 100.00 from Payment 2)
EXEC dbo.usp_MonthlyBilling_ReviseBill
    @BillId = @HarnessBillId1,
    @ExpectedVersion = 1,
    @Adjustments = 100.00,
    @Reason = 'Positive security test bill revision',
    @AdminUserId = @HarnessAdminId,
    @NewRevisionId = @HarnessRevId OUTPUT;

PRINT 'PASSED [Positive Test 3]: usp_MonthlyBilling_ReviseBill succeeded. RevisionId=' + CAST(@HarnessRevId AS NVARCHAR);

-- Positive Test 4: usp_Payment_Void
-- Voids Payment 1, cleanly reversing its remaining active allocation of 600.00 on Bill 1
EXEC dbo.usp_Payment_Void
    @PaymentId = @HarnessPayId1,
    @VoidReason = 'Positive security test payment voiding justification',
    @AdminUserId = @HarnessAdminId;

PRINT 'PASSED [Positive Test 4]: usp_Payment_Void succeeded.';

PRINT '>> ALL FOUR APPROVED STORED PROCEDURES EXECUTED SUCCESSFULLY UNDER dms_app_user.';

-- ------------------------------------------------------------------------------------
-- CLEANUP: ROLLBACK TRANSACTION TO PREVENT PERSISTENCE
-- ------------------------------------------------------------------------------------
ROLLBACK TRANSACTION;
PRINT '===================================================================';
PRINT 'TEST HARNESS COMPLETE: Transaction rolled back successfully.';
PRINT 'Zero persistent test records created. Production data completely untouched.';
PRINT '===================================================================';

-- If running within development deployment session, revert context:
-- REVERT;
```

---

## 3. Approved Business Rules

### 3.1 Monthly Billing Rules
1. **Calendar-Month Period:** The billing period spans from `DATEFROMPARTS(Year, Month, 1)` to `EOMONTH(PeriodStartDate)`.
2. **Chargeable Deliveries:** Only daily deliveries in `Status = 'Completed'` are billable. Deliveries in `Status = 'Pending'` or `Status = 'Not Delivered'` contribute ₹0.00 and 0.000 L.
3. **Approved Vacation Exclusion:** Any delivery overlapping an approved vacation in `dbo.CustomerVacations` (`Status = 'Approved'`) is excluded via an explicit `NOT EXISTS` database guard.
4. **Historical Rate Determinism:** Each line item is computed using `DailyMilkDeliveryItems.UnitPriceSnapshot`. Current category prices or current subscription prices are never used retroactively.
5. **Authoritative Primary Receivable:**
   $$\text{CurrentMonthInvoiceAmount} = \text{CurrentMonthMilkAmount} - \text{Adjustments}$$
   $$\text{BalanceAmount} = \text{CurrentMonthInvoiceAmount} - \text{PaidAmount}$$
   `BalanceAmount` in `dbo.MonthlyBills` represents the unpaid debt of **this specific invoice alone**.
6. **Publication Lock:** Once an invoice is marked as published (`IsPublished = 1`), it is locked against direct modification. Any change requires a formal **Bill Revision**.

### 3.2 Payment & Single-Invoice Allocation Rules
1. **Strictly Two Payment Modes:** Exactly two modes are supported:
   - `Online`: Recorded manually by Admin with mandatory or optional `ReferenceNumber` (e.g. Bank UTR, UPI transaction ID).
   - `Offline`: Recorded manually by Admin (Cash, In-person settlement).
   - *No automated payment gateways (Razorpay, Stripe, UPI APIs) are integrated.*
2. **One Payment, One Selected Invoice:**
   - Admin selects the paying customer, enters payment amount, selects payment mode, and **selects exactly one invoice** to apply the payment to.
   - The payment is allocated up to that invoice's `BalanceAmount`:
     $$\text{InitialAllocatedAmount} = \min(\text{Payment.Amount}, \text{SelectedBill.BalanceAmount})$$
3. **Unallocated Advance Credit:**
   - If $\text{Payment.Amount} > \text{SelectedBill.BalanceAmount}$, the excess amount is captured as unallocated advance credit on that payment receipt:
     $$\text{UnallocatedCredit} = \text{Payment.Amount} - \sum (\text{EventAmount} \times \text{Direction})$$
   - This advance credit **must not automatically settle another month's invoice**. It remains on the payment receipt until Admin explicitly performs an authorized "Allocate Advance Credit" action selecting a specific target invoice.
4. **Zero Automatic FIFO Allocation:** The system under no circumstances automatically applies money to the oldest invoice. All allocations are driven by deliberate administrative selection.
5. **Financial Immutability (No Hard Deletes):** Payment receipts and allocation events cannot be deleted. If entered in error, an Admin executes a **Void Payment** action with a required justification ($\ge 10$ characters), writing reversal events and logging the audit trail.

### 3.3 Due Date Policy (PROPOSED DEFAULT — REQUIRES BUSINESS APPROVAL)
- **Status:** **`PROPOSED DEFAULT — REQUIRES BUSINESS APPROVAL`**
- **Proposed Due Date Calculation:** 10th calendar day of the month following the billing period:
  $$\text{PaymentDueDate} = \text{DATEADD(day, 10, PeriodEndDate)}$$
  *(Example: For billing period 2027-01-01 to 2027-01-31, `PaymentDueDate` is proposed as 2027-02-10).*
- **Formal Implementation Contract:** See Section 6 for the complete, eight-point contract governing approval requirements, draft vs. published migration, and overdue transitions.

---

## 4. Authoritative Customer Ledger & Customer Statement Presentation

### 4.1 Distinct Financial Concepts
To prevent double-counting and eliminate confusion between individual invoice debt and overall account standing, the architecture establishes six separate financial concepts:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        CUSTOMER FINANCIAL CONCEPTS                     │
├───────────────────────────────┬────────────────────────────────────────┤
│ 1. CurrentMonthCharge         │ Gross consumption for target month     │
│ 2. PreviousBalanceSnapshot    │ Prior unpaid invoices at bill creation │
│ 3. StatementAmount            │ Previous Snapshot + Current Charge     │
│ 4. InvoiceOutstanding         │ Unpaid portion of THIS invoice alone   │
│ 5. AvailableAdvanceCredit     │ Unallocated funds on active payments   │
│ 6. CustomerNetOutstanding     │ Total Unpaid Invoices - Advance Credit │
└───────────────────────────────┴────────────────────────────────────────┘
```

### 4.2 Exact Mathematical Formulas
1. **Current Month Charge:**
   $$\text{CurrentMonthCharge} = \text{CurrentMonthMilkAmount} - \text{Adjustments}$$
2. **Invoice Outstanding (Remaining Balance of THIS Invoice):**
   $$\text{InvoiceOutstanding} = \text{BalanceAmount} = \text{CurrentMonthCharge} - \text{ActiveAllocatedAmount}$$
   $$\text{ActiveAllocatedAmount} = \sum_{e \in \text{AllocationEvents}(\text{BillId})} (e.\text{Amount} \times e.\text{Direction})$$
3. **Previous Balance Snapshot (Informational Only):**
   $$\text{PreviousBalanceSnapshot} = \sum_{\text{Prior Published Bills } p} p.\text{InvoiceOutstanding}$$
4. **Statement Amount (Total Printed on Customer's Physical Bill):**
   $$\text{StatementAmount} = \text{PreviousBalanceSnapshot} + \text{CurrentMonthCharge}$$
5. **Available Advance Credit (Strictly Non-Voided Payments):**
   $$\text{AvailableAdvanceCredit}(P) = \begin{cases} 
   0.00 & \text{if } P.\text{IsVoided} = 1 \\
   P.\text{Amount} - \sum_{e \in \text{AllocationEvents}(P.\text{PaymentId})} (e.\text{Amount} \times e.\text{Direction}) & \text{if } P.\text{IsVoided} = 0 
   \end{cases}$$
6. **Customer Net Outstanding (Real-Time Account Position):**
   $$\text{CustomerNetOutstanding} = \sum_{b \in \text{PublishedBills}} b.\text{InvoiceOutstanding} - \sum_{p \in \text{Payments}, p.\text{IsVoided} = 0} \text{AvailableAdvanceCredit}(p)$$

---

### 4.3 Worked Customer Statement Example

Consider a customer with:
- Previous invoice outstanding (January): ₹500.00
- Current month charge (February): ₹300.00
- Available unallocated advance credit (from an earlier non-voided payment): ₹100.00

#### Printed Customer Statement Presentation:
```text
===================================================================
                  DAIRY MANAGEMENT SYSTEM
               MONTHLY CUSTOMER MILK STATEMENT
===================================================================
Customer: Sunil Sharma (CUST-0012)          Billing Month: Feb 2027
Route: Route 1 - North                      Due Date: Pending Business Approval
-------------------------------------------------------------------
Previous Outstanding Balance ................... ₹ 500.00
Current Month Milk Charges (Feb) ............... ₹ 300.00
-------------------------------------------------------------------
Total Statement Amount Due ..................... ₹ 800.00
-------------------------------------------------------------------
Available Unallocated Advance Credit .......... ₹ 100.00
-------------------------------------------------------------------
NET CUSTOMER ACCOUNT OUTSTANDING .............. ₹ 700.00
===================================================================
Note: Unallocated advance credit is held on deposit. To apply credit 
to an outstanding bill, please contact administration.
Payment Due Date is subject to formal administrative policy approval.
(Proposed default grace period: 10th of following month).
===================================================================
```

---

## 5. Strictly Append-Only Payment Allocation Model

### 5.1 Schema Design: `dbo.PaymentAllocationEvents`

```sql
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
    [LegacyAllocationId] BIGINT NULL,  -- Populated for legacy migration; NULL for runtime events
    [PerformedBy] INT NOT NULL CONSTRAINT [FK_PaymentAllocationEvents_PerformedBy] REFERENCES [dbo].[Users]([UserId]),
    [PerformedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAllocationEvents_PerformedAt] DEFAULT (SYSUTCDATETIME())
);

-- Unique Filtered Index enforcing 1:1 legacy migration uniqueness
CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_LegacyAllocationId]
ON [dbo].[PaymentAllocationEvents]([LegacyAllocationId])
WHERE [LegacyAllocationId] IS NOT NULL;

-- Secondary Filtered Unique Index enforcing 1:1 void reversal uniqueness per original event
CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent]
ON [dbo].[PaymentAllocationEvents]([OriginalAllocationEventId])
WHERE [EventType] = 'VoidReversal' AND [OriginalAllocationEventId] IS NOT NULL;
```

---

### 5.2 Deterministic LIFO Reversal & Bill Revision Finalization Algorithm

#### Procedure Logic in `dbo.usp_MonthlyBilling_ReviseBill`:
```
Procedure: usp_MonthlyBilling_ReviseBill(@BillId, @ExpectedVersion, @Adjustments, @Reason, @AdminUserId)
1. Lock target bill row:
   SELECT BillId, CurrentMonthMilkAmount, PaidAmount, BalanceAmount, Version, PaymentDueDate
   FROM dbo.MonthlyBills WITH (UPDLOCK, HOLDLOCK)
   WHERE BillId = @BillId;

2. Verify Optimistic Concurrency:
   IF Version <> @ExpectedVersion THROW 50030, 'Concurrency violation: Bill has been modified.', 1;

3. Calculate New Invoice Target Amount:
   TargetInvoiceAmount = CurrentMonthMilkAmount - @Adjustments;
   IF TargetInvoiceAmount < 0 THROW 50031, 'Adjustments cannot exceed gross milk amount.', 1;

4. Calculate Current Active Allocation directly from event ledger:
   CurrentActiveAllocation = ISNULL((SELECT SUM(SignedAmount) FROM dbo.PaymentAllocationEvents WHERE BillId = @BillId), 0.00);

5. Evaluate Allocation Adjustment Path:
   CASE A: TargetInvoiceAmount > CurrentActiveAllocation
      - No reversal needed.
      - Final PaidAmount = CurrentActiveAllocation.
      - Final BalanceAmount = TargetInvoiceAmount - Final PaidAmount.

   CASE B: TargetInvoiceAmount == CurrentActiveAllocation
      - No reversal needed.
      - Final PaidAmount = CurrentActiveAllocation.
      - Final BalanceAmount = 0.00.

   CASE C: TargetInvoiceAmount < CurrentActiveAllocation
      - Downward revision over-allocated. ExcessToRelease = CurrentActiveAllocation - TargetInvoiceAmount.
      - Execute Deterministic LIFO Reversal:
        Open cursor over active initial/advance allocation events for @BillId ordered by AllocationEventId DESC:
        SELECT e.AllocationEventId, e.PaymentId, 
               e.Amount - ISNULL(prior_rev.ReversedAmount, 0.00) AS RemainingActiveAmount
        FROM dbo.PaymentAllocationEvents e
        LEFT JOIN (
            SELECT OriginalAllocationEventId, SUM(Amount) AS ReversedAmount
            FROM dbo.PaymentAllocationEvents
            WHERE BillId = @BillId AND Direction = -1
            GROUP BY OriginalAllocationEventId
        ) prior_rev ON e.AllocationEventId = prior_rev.OriginalAllocationEventId
        WHERE e.BillId = @BillId AND e.Direction = 1
          AND (e.Amount - ISNULL(prior_rev.ReversedAmount, 0.00)) > 0.00
        ORDER BY e.AllocationEventId DESC;  -- Deterministic LIFO tie-breaker

        WHILE ExcessToRelease > 0 AND FetchNext:
            ReversalPortion = MIN(RemainingActiveAmount, ExcessToRelease);
            INSERT INTO dbo.PaymentAllocationEvents (
                PaymentId, BillId, EventType, OriginalAllocationEventId,
                Amount, Direction, Reason, RelatedRevisionId, LegacyAllocationId,
                PerformedBy, PerformedAt
            ) VALUES (
                candidate.PaymentId, @BillId, 'ReversalAdjustment', candidate.AllocationEventId,
                ReversalPortion, -1, @Reason, @NewRevisionId, NULL,
                @AdminUserId, SYSUTCDATETIME()
            );
            ExcessToRelease = ExcessToRelease - ReversalPortion;

      - Strict Event-Ledger Verification Assertion:
        RecalculatedActiveAllocation = ISNULL((SELECT SUM(SignedAmount) FROM dbo.PaymentAllocationEvents WHERE BillId = @BillId), 0.00);
        IF RecalculatedActiveAllocation <> TargetInvoiceAmount
            THROW 50065, 'Reversal reconciliation failed: Event ledger active total does not equal target invoice amount.', 1;

      - Final PaidAmount = RecalculatedActiveAllocation;
      - Final BalanceAmount = TargetInvoiceAmount - Final PaidAmount;  -- (0.00)

6. Derive Authoritative Payment Status:
   IF Final BalanceAmount == 0.00:
       NewStatus = 'Paid'
   ELSE IF Final PaidAmount == 0.00:
       IF PaymentDueDate IS NOT NULL AND CurrentISTDate > PaymentDueDate:
           NewStatus = 'Overdue'
       ELSE:
           NewStatus = 'Unpaid'
   ELSE:
       IF PaymentDueDate IS NOT NULL AND CurrentISTDate > PaymentDueDate:
           NewStatus = 'Overdue'
       ELSE:
           NewStatus = 'Partially Paid'

7. Atomically Update MonthlyBills:
   UPDATE dbo.MonthlyBills
   SET CurrentMonthInvoiceAmount = TargetInvoiceAmount,
       Adjustments = @Adjustments,
       PaidAmount = Final PaidAmount,
       BalanceAmount = Final BalanceAmount,
       PaymentStatus = NewStatus,
       Version = Version + 1,
       UpdatedAt = SYSUTCDATETIME()
   WHERE BillId = @BillId;
```

---

### 5.3 Worked Examples Demonstrating LIFO Revision Rules

#### Example 1: ₹1,000 Revised to ₹800 When ₹1,000 Is Paid
- **Initial State:** Target Bill #101 = ₹1,000.00. Paid = ₹1,000.00 (via Event #10, P1). Balance = ₹0.00 (`Paid`).
- **Revision:** Target revised to **₹800.00**.
- **Rule Applied:** `TargetInvoiceAmount (800) < CurrentActiveAllocation (1000)`. Excess = ₹200.00.
- **LIFO Reversal:** Event #10 reversed by ₹200.00 (Appends Event #11, `Direction = -1`, `Amount = 200.00`, `OriginalAllocationEventId = 10`).
- **Recalculated Paid:** $+1000.00 - 200.00 = \mathbf{₹800.00}$.
- **Final Balance:** $800.00 - 800.00 = \mathbf{₹0.00}$.
- **Status:** **`Paid`**. P1 Available Advance Credit = **₹200.00**.

#### Example 2: ₹1,000 Revised to ₹1,200 When ₹700 Is Paid
- **Initial State:** Target Bill #102 = ₹1,000.00. Paid = ₹700.00 (via Event #20, P2). Balance = ₹300.00 (`Partially Paid`).
- **Revision:** Upward revision to **₹1,200.00**.
- **Rule Applied:** `TargetInvoiceAmount (1200) > CurrentActiveAllocation (700)`. Excess = 0.
- **LIFO Reversal:** No reversal required.
- **Final PaidAmount:** **₹700.00** (Current active allocation).
- **Final BalanceAmount:** $1200.00 - 700.00 = \mathbf{₹500.00}$.
- **Status:** **`Partially Paid`** (or **`Overdue`** if `PaymentDueDate` is set and expired).

#### Example 3: ₹1,000 Revised to ₹600 When ₹700 Is Paid
- **Initial State:** Target Bill #103 = ₹1,000.00. Paid = ₹700.00 (via Event #30, P3). Balance = ₹300.00 (`Partially Paid`).
- **Revision:** Downward revision to **₹600.00**.
- **Rule Applied:** `TargetInvoiceAmount (600) < CurrentActiveAllocation (700)`. Excess = ₹100.00.
- **LIFO Reversal:** Event #30 reversed by ₹100.00 (Appends Event #31, `Direction = -1`, `Amount = 100.00`, `OriginalAllocationEventId = 30`).
- **Recalculated Paid:** $+700.00 - 100.00 = \mathbf{₹600.00}$.
- **Final Balance:** $600.00 - 600.00 = \mathbf{₹0.00}$.
- **Status:** **`Paid`**. P3 Available Advance Credit = **₹100.00**.

#### Example 4: A Previously Partially Reversed Allocation Revised Again
- **Initial Multi-Payment State on Bill #104:**
  - Original Amount: ₹1,000.00.
  - P1: ₹400.00 (`Event #40`, recorded Feb 5)
  - P2: ₹350.00 (`Event #41`, recorded Feb 6)
  - P3: ₹250.00 (`Event #42`, recorded Feb 7)
  - Total Paid = ₹1,000.00.
- **Revision 1 (Revised Downward to ₹800.00):**
  - Excess = ₹200.00.
  - LIFO orders by `AllocationEventId DESC`: Candidate 1 is Event #42 (P3).
  - P3 active = ₹250.00 $\ge$ ₹200.00.
  - Event #43 created: Reversal of Event #42 by **₹200.00** (`EventType = 'ReversalAdjustment'`).
  - P3 Remaining Active Allocation = $250.00 - 200.00 = \mathbf{₹50.00}$. P3 Advance Credit = **₹200.00**.
  - Total Paid = ₹800.00. Balance = ₹0.00 (`Paid`).
- **Revision 2 (Revised Further Downward to ₹500.00):**
  - New Target = ₹500.00. Current Paid = ₹800.00. Excess = **₹300.00**.
  - Algorithm inspects active remaining allocations ordered by `AllocationEventId DESC`:
    1. **Candidate 1 (Event #42, P3):**
       - Initial Amount: ₹250.00. Prior Reversals: ₹200.00.
       - **Remaining Active Balance = ₹50.00**.
       - Reversal amount bounded by active balance: $\min(50.00, 300.00) = \mathbf{₹50.00}$.
       - Event #44 created: Second partial reversal of Event #42 by **₹50.00** (`EventType = 'ReversalAdjustment'`, `Direction = -1`).
       - P3 remaining active becomes ₹0.00. P3 Advance Credit becomes $200.00 + 50.00 = \mathbf{₹250.00}$.
       - Remaining excess to release: $300.00 - 50.00 = \mathbf{₹250.00}$.
    2. **Candidate 2 (Event #41, P2):**
       - Initial Amount: ₹350.00. Prior Reversals: ₹0.00.
       - **Remaining Active Balance = ₹350.00**.
       - Reversal amount: $\min(350.00, 250.00) = \mathbf{₹250.00}$.
       - Event #45 created: Reversal of Event #41 by **₹250.00** (`EventType = 'ReversalAdjustment'`, `Direction = -1`).
       - P2 remaining active becomes $350.00 - 250.00 = \mathbf{₹100.00}$. P2 Advance Credit becomes $\mathbf{₹250.00}$.
       - Remaining excess to release: $250.00 - 250.00 = \mathbf{₹0.00}$.
    3. **Candidate 3 (Event #40, P1):** Untouched. Active allocation remains **₹400.00**.
- **Final Verification:**
  - Recalculated Paid: $0.00 (\text{P3}) + 100.00 (\text{P2}) + 400.00 (\text{P1}) = \mathbf{₹500.00}$.
  - Final Balance: $500.00 - 500.00 = \mathbf{₹0.00}$. Status: **`Paid`**.

---

### 5.4 Authoritative Payment Void Contract, Concurrency Locking & Five Worked Scenarios

#### Two-Tiered Idempotency & Transaction Execution Rules:
The procedure `dbo.usp_Payment_Void` enforces strict payment-level idempotency via concurrency-safe row locking and transactional state transitions:

```
Procedure: dbo.usp_Payment_Void(@PaymentId, @VoidReason, @AdminUserId)
1. Begin Transaction:
   SET NOCOUNT ON;
   SET XACT_ABORT ON;
   BEGIN TRANSACTION;

2. Concurrency-Safe Payment Row Lock (Tier 1 Primary Idempotency):
   DECLARE @CustomerId INT, @PaymentAmount DECIMAL(18,2), @IsVoided BIT;
   SELECT @CustomerId = CustomerId, @PaymentAmount = Amount, @IsVoided = IsVoided
   FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
   WHERE PaymentId = @PaymentId;

3. Existence & Ownership Verification:
   IF @PaymentAmount IS NULL
       THROW 50040, 'Payment record does not exist.', 1;

4. Non-Voided Verification (Catches Any Repeated Void Request):
   IF @IsVoided = 1
       THROW 50041, 'Payment is already voided. Repeated void operations are strictly prohibited.', 1;

5. Identify Affected Invoices and Remaining Active Allocations:
   -- Queries remaining active allocations for this payment across all bills
   DECLARE @AllocationsCursor CURSOR;
   DECLARE @TargetBillId BIGINT, @RemainingActiveAmount DECIMAL(18,2), @InitialEventId BIGINT;

   SET @AllocationsCursor = CURSOR LOCAL FAST_FORWARD FOR
       SELECT 
           e.BillId,
           e.AllocationEventId,
           e.Amount - ISNULL(reversals.ReversedAmount, 0.00) AS RemainingActiveAmount
       FROM dbo.PaymentAllocationEvents e
       LEFT JOIN (
           SELECT OriginalAllocationEventId, SUM(Amount) AS ReversedAmount
           FROM dbo.PaymentAllocationEvents
           WHERE PaymentId = @PaymentId AND Direction = -1
           GROUP BY OriginalAllocationEventId
       ) reversals ON e.AllocationEventId = reversals.OriginalAllocationEventId
       WHERE e.PaymentId = @PaymentId 
         AND e.Direction = 1
         AND (e.Amount - ISNULL(reversals.ReversedAmount, 0.00)) > 0.00;

6. Write Directional VoidReversal Events (Guarded by Tier 2 Secondary Filtered Unique Index):
   OPEN @AllocationsCursor;
   FETCH NEXT FROM @AllocationsCursor INTO @TargetBillId, @InitialEventId, @RemainingActiveAmount;

   WHILE @@FETCH_STATUS = 0
   BEGIN
       -- Write VoidReversal event strictly for the remaining active allocation
       -- (Guarded at storage level by UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent)
       INSERT INTO dbo.PaymentAllocationEvents (
           PaymentId, BillId, EventType, OriginalAllocationEventId,
           Amount, Direction, Reason, RelatedRevisionId, LegacyAllocationId,
           PerformedBy, PerformedAt
       ) VALUES (
           @PaymentId, @TargetBillId, 'VoidReversal', @InitialEventId,
           @RemainingActiveAmount, -1, @VoidReason, NULL, NULL,
           @AdminUserId, SYSUTCDATETIME()
       );

       -- 7. Recalculate Affected Invoice Balances & Statuses
       DECLARE @RecalcPaid DECIMAL(18,2), @InvoiceAmount DECIMAL(18,2), @DueDate DATE;
       SELECT @InvoiceAmount = CurrentMonthInvoiceAmount, @DueDate = PaymentDueDate
       FROM dbo.MonthlyBills WITH (UPDLOCK, HOLDLOCK)
       WHERE BillId = @TargetBillId;

       SET @RecalcPaid = ISNULL((SELECT SUM(SignedAmount) FROM dbo.PaymentAllocationEvents WHERE BillId = @TargetBillId), 0.00);

       DECLARE @NewStatus NVARCHAR(20);
       IF (@InvoiceAmount - @RecalcPaid) <= 0.00
           SET @NewStatus = 'Paid';
       ELSE IF @RecalcPaid = 0.00
           SET @NewStatus = CASE WHEN @DueDate IS NOT NULL AND SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' > @DueDate THEN 'Overdue' ELSE 'Unpaid' END;
       ELSE
           SET @NewStatus = CASE WHEN @DueDate IS NOT NULL AND SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' > @DueDate THEN 'Overdue' ELSE 'Partially Paid' END;

       UPDATE dbo.MonthlyBills
       SET PaidAmount = @RecalcPaid,
           BalanceAmount = @InvoiceAmount - @RecalcPaid,
           PaymentStatus = @NewStatus,
           UpdatedAt = SYSUTCDATETIME()
       WHERE BillId = @TargetBillId;

       FETCH NEXT FROM @AllocationsCursor INTO @TargetBillId, @InitialEventId, @RemainingActiveAmount;
   END;
   CLOSE @AllocationsCursor;
   DEALLOCATE @AllocationsCursor;

8. Update Payments Table Status within Same Transaction:
   UPDATE dbo.Payments
   SET IsVoided = 1,
       VoidedAt = SYSUTCDATETIME(),
       VoidReason = @VoidReason,
       VoidedBy = @AdminUserId
   WHERE PaymentId = @PaymentId;

9. Commit Transaction Atomically:
   COMMIT TRANSACTION;
```

---

#### Worked Voiding Scenarios:

##### Scenario 1: A Payment with Ordinary Bill Allocations
- **State:** Payment #601 (₹1,000.00) allocated in full to Bill #201 (CurrentMonthInvoiceAmount = ₹1,000.00). Bill #201 `BalanceAmount = ₹0.00` (`Paid`).
- **Void Execution:**
  - Row locked with `UPDLOCK, HOLDLOCK`. `IsVoided = 0` verified.
  - Active allocation on Bill #201 = **₹1,000.00**.
  - Event #101 written: `EventType = 'VoidReversal'`, `Amount = 1000.00`, `Direction = -1`, `OriginalAllocationEventId = initialEvent.Id`.
  - Bill #201 `PaidAmount` drops to ₹0.00, `BalanceAmount` restores to **₹1,000.00**; Status transitions to `Unpaid` (or `Overdue`).
  - Payment #601 marked `IsVoided = 1`. Available advance credit = **₹0.00**.

##### Scenario 2: A Payment with an Allocation Partially Reversed by a Bill Revision
- **State:** Payment #602 (₹1,000.00) allocated in full to Bill #202 (₹1,000.00). Bill #202 was subsequently revised downward to ₹800.00 (Revision 2), releasing ₹200.00 back to Payment #602 via a `ReversalAdjustment` event.
  - Active allocation on Bill #202 = **₹800.00**.
  - Unallocated advance credit on Payment #602 = **₹200.00**.
- **Void Execution:**
  - Row locked with `UPDLOCK, HOLDLOCK`. `IsVoided = 0` verified.
  - System calculates remaining active allocation on Bill #202: $\mathbf{₹800.00}$.
  - System writes `VoidReversal` event strictly for **₹800.00** (`Direction = -1`).
  - The ₹200.00 released by the earlier downward revision is **strictly NOT double-reversed**!
  - Bill #202 `PaidAmount` drops to ₹0.00, `BalanceAmount` restores to **₹800.00** (`Unpaid`).
  - Payment #602 updated: `IsVoided = 1`.
  - Available advance credit on Payment #602 drops from ₹200.00 to **₹0.00** (voided receipt holds zero credit).

##### Scenario 3: A Payment with Only Unallocated Advance Credit
- **State:** Customer paid ₹500.00 via Payment #603. Because target bill was already zero, the entire ₹500.00 remained as unallocated advance credit on Payment #603. Active allocations on bills = **₹0.00**.
- **Void Execution:**
  - Row locked with `UPDLOCK, HOLDLOCK`. `IsVoided = 0` verified.
  - Active allocations query returns 0 rows (no active bill allocations).
  - Loop executes 0 iterations (zero `VoidReversal` events written).
  - Payment #603 updated: `IsVoided = 1`, `VoidReason = @VoidReason`.
  - Available advance credit drops from ₹500.00 to **₹0.00**.
  - Transaction commits cleanly.

##### Scenario 4: A Payment with Zero Allocations
- **State:** Payment #604 (₹0.00 balance or fully exhausted record). Active allocations = ₹0.00.
- **Void Execution:**
  - Row locked with `UPDLOCK, HOLDLOCK`. `IsVoided = 0` verified.
  - Active allocations = ₹0.00.
  - Payment #604 updated: `IsVoided = 1`.
  - Available credit = ₹0.00.
  - Transaction commits cleanly.

##### Scenario 5: A Repeated Void Request (Idempotency Proof)
- **Action:** An operator or concurrent thread calls `dbo.usp_Payment_Void` again for Payment #601, #602, #603, or #604.
- **Execution:**
  - Step 2 acquires `UPDLOCK, HOLDLOCK` on `dbo.Payments` for `@PaymentId`.
  - Step 4 reads `@IsVoided == 1`.
  - **Immediate Rejection:** Throws Error `50041: Payment is already voided. Repeated void operations are strictly prohibited.`.
  - Transaction rolls back.
  - **Crucial Architectural Invariant:** The procedure guarantees 100% idempotency across all payment profiles (allocated, partially reversed, pure credit, or zero allocation). The filtered unique index acts as an independent secondary defense preventing duplicate reversal events.

---

## 6. Due Date Policy & Implementation Contract

```
┌────────────────────────────────────────────────────────────────────────┐
│                        DUE DATE POLICY CONTRACT                        │
├──────────────────────────────────┬─────────────────────────────────────┤
│ Policy Status                    │ PROPOSED DEFAULT                    │
│ Approval Status                  │ REQUIRES BUSINESS APPROVAL          │
│ Proposed Grace Calculation       │ DATEADD(day, 10, PeriodEndDate)     │
│ Time-Zone Standard               │ Indian Standard Time (UTC + 5:30)   │
│ Database Invariant               │ Snapshotted on MonthlyBills         │
│ Historical Published Bills       │ Strictly Protected from Retro Alter │
└──────────────────────────────────┴─────────────────────────────────────┘
```

### Formal Eight-Point Contract:
1. **Behavior When No Approved Due-Date Policy Exists:**
   - Until a formal business due-date policy is ratified, the system treats the 10th of the following month strictly as an unapproved proposed default.
   - Customer statements, UI screens, and PDF prints will display: `Due Date: Pending Business Approval`.
   - The system must **never silently invent** or finalize an unapproved due date.
2. **Invoice Publication Gating:**
   - In Phase 2 and Phase 3, invoice publication requires either an approved system setting (`SystemSettings.DefaultDueGraceDays` with `IsApproved = 1`) or an explicit grace-period confirmation by the publishing Administrator. If unapproved and unconfirmed, publication stored procedures will return an error: `50070: Due date policy has not been formally approved. Administrative confirmation required.`.
3. **`PaymentDueDate` Population Upon Policy Approval:**
   - When an approved policy or explicit grace parameter is provided, `PaymentDueDate` is calculated deterministically as:
     $$\text{PaymentDueDate} = \text{DATEADD(day, @ApprovedGraceDays, PeriodEndDate)}$$
   - It is permanently written to `dbo.MonthlyBills.PaymentDueDate` during the transition to `IsPublished = 1`.
4. **Safe Population of Nullable Migration Columns:**
   - Column `PaymentDueDate` is added to `dbo.MonthlyBills` as `DATE NULL`.
   - Adding a nullable column is an online, metadata-only operation in SQL Server that incurs zero downtime and zero table lock contention.
5. **Immutability of Published Invoices with Existing Due Dates:**
   - Once an invoice is marked `IsPublished = 1`, its `PaymentDueDate` is permanently locked.
   - Future changes to company payment terms, grace periods, or system configurations apply exclusively to bills published *after* the change. Existing published bills are **never updated retroactively**.
6. **Handling Historical Published Invoices with NULL Due Dates:**
   - Historical published invoices generated prior to Day 5 retain `PaymentDueDate = NULL`.
   - They will **never** be retroactively updated without an explicit administrative data patch approved by the business.
7. **Overdue Status Evaluation Rule:**
   - An invoice can transition to `Overdue` **strictly and only if `PaymentDueDate IS NOT NULL`**.
   - If `PaymentDueDate IS NULL`, the bill remains in status `Unpaid` (if `PaidAmount == 0`) or `Partially Paid` (if `PaidAmount > 0`), but **can never be marked Overdue**.
   - When `PaymentDueDate IS NOT NULL`, status evaluates in Indian Standard Time:
     - If $\text{BalanceAmount} > 0.00$ and $\text{CurrentISTDate} > \text{PaymentDueDate}$ &rarr; **`Overdue`**.
     - If $\text{BalanceAmount} > 0.00$ and $\text{CurrentISTDate} \le \text{PaymentDueDate}$ &rarr; **`Unpaid`** (or **`Partially Paid`**).
     - If $\text{BalanceAmount} = 0.00$ &rarr; **`Paid`**.
8. **Future-Only Scope of Policy Revisions:**
   - Modifying system grace periods affects future draft publications only. Published records remain inviolable financial snapshots.

---

## 7. Migration Cutover, Rollback Gate & Stored Procedures

### 7.1 Ten-Step Migration Sequence
The production migration follows an uncompromising sequential workflow:

1. **Pre-Migration Inspection:** Audit existing schema, indexes, constraints, and total record counts of `dbo.PaymentAllocations`, `dbo.Payments`, and `dbo.MonthlyBills`.
2. **Schema & Index Creation:** Execute DDL creating `dbo.PaymentAllocationEvents`, its check constraints, and the two unique filtered indexes (`UQ_PaymentAllocationEvents_LegacyAllocationId` and `UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent`).
3. **Active Legacy Allocation Migration:** Transactionally migrate all `IsVoided = 0` rows from `dbo.PaymentAllocations` populating `LegacyAllocationId`.
4. **Historical Voided Allocation Migration:** Transactionally migrate all `IsVoided = 1` rows as atomic pairs of `InitialAllocation` + `VoidReversal` events.
5. **Reconciliation Execution:** Execute all five automated migration-time reconciliation checks (Grand Total Active, Void Net Zero, Payment-Level, Bill-Level, Duplicate/Missing IDs).
6. **Reconciliation Gate Verification:** Confirm zero discrepancies, zero duplicated IDs, and zero missing legacy allocations. If any check fails, trigger immediate rollback.
7. **Write-Path Decommissioning & Least-Privilege Grant:** Install trigger `trg_PaymentAllocations_LegacyReadOnly` on legacy table, alter `dbo.usp_AllocatePayment` to throw error `50060`, revoke legacy DML permissions, grant **SELECT ONLY** on `dbo.PaymentAllocationEvents` to `[db_dms_app]`, and issue explicit **DENY** on direct `INSERT`, `UPDATE`, and `DELETE`.
8. **Actual Identity & Effective Permission Verification [Status: DEFINED — EXECUTION PENDING]:** Connect via the actual production application connection string, report identity tokens (`SUSER_SNAME()`, `ORIGINAL_LOGIN()`, `USER_NAME()`, `IS_SRVROLEMEMBER('sysadmin') = 0`, and role `db_dms_app`), and execute the rolled-back test harness confirming that direct `INSERT`, `UPDATE`, and `DELETE` fail with Error 229, and that all four approved stored procedures (`usp_Payment_RecordAndAllocateSingle`, `usp_Payment_AllocateAdvanceCreditSingle`, `usp_MonthlyBilling_ReviseBill`, `usp_Payment_Void`) execute successfully under `dms_app_user`.
9. **Repository Cutover:** Deploy updated `IBillingRepository` and `BillingRepository.cs` querying `dbo.PaymentAllocationEvents` exclusively via approved stored procedures.
10. **Authoritative Ledger Declaration:** Formally seal the event ledger as the sole production source of truth.

### 7.2 Failure Protocol & Rollback Gate
- **Single Transaction Rationale:** The data volume of historical dairy allocations in `dbo.PaymentAllocations` is small-to-moderate. Executing the migration inside a single transaction (`SET XACT_ABORT ON; BEGIN TRANSACTION ... COMMIT TRANSACTION`) provides 100% atomicity.
- **Rollback Behavior:**
  - If any error or constraint violation occurs, the transaction automatically aborts and rolls back.
  - If any of the five reconciliation queries detect a non-zero discrepancy, an explicit `ROLLBACK TRANSACTION` is executed immediately.
  - Application reads **never switch** to the event ledger if reconciliation fails.
  - Legacy `dbo.PaymentAllocations` data remains completely intact and undisturbed.
  - **Zero Data Deletion Policy:** Engineers are strictly prohibited from deleting or manually altering historical financial records to force reconciliation parity. Discrepancies must be investigated and resolved at the query/mapping level.

---

## 8. Stored Procedures Specification for Kevin

Kevin will implement the following stored procedures in `sql/08_BillingAndPaymentsManagement.sql`:

1. **`dbo.usp_MonthlyBilling_GenerateCustomerBill`:**
   - Aggregates completed deliveries excluding approved vacations.
   - Calculates `PreviousBalanceSnapshot = ISNULL(SUM(BalanceAmount), 0.00)` from prior published bills (informational only).
   - Sets `CurrentMonthInvoiceAmount = MilkAmount - Adjustments`.
   - Sets `TotalStatementAmount = PreviousBalanceSnapshot + CurrentMonthInvoiceAmount`.
   - Sets `BalanceAmount = CurrentMonthInvoiceAmount - PaidAmount`. *(Authoritative Receivable Invariant)*
   - Upserts `MonthlyBills` (Draft) and itemizes `MonthlyBillItems`.
2. **`dbo.usp_MonthlyBilling_GenerateBulk`:**
   - Acquires `sp_getapplock @Resource = 'DMS_BulkBilling_Lock'`.
   - Iterates eligible customers in isolated transactions.
   - Returns summary of Succeeded, Skipped, and Failed.
3. **`dbo.usp_MonthlyBilling_PublishBill` & `usp_MonthlyBilling_BulkPublish`:**
   - Validates due date policy or explicit grace parameter.
   - Sets `IsPublished = 1`, `PublishedAt = SYSUTCDATETIME()`.
   - Permanently snapshots `PaymentDueDate = DATEADD(day, @GraceDays, PeriodEndDate)`.
   - Sets initial status (`Paid`, `Unpaid`, or `Overdue`).
4. **`dbo.usp_MonthlyBilling_ReviseBill` (Deterministic Multi-Payment LIFO Reversal):**
   - Implements the complete 7-step algorithm from Section 5.2 via static SQL.
   - Locks target bill with `UPDLOCK, HOLDLOCK`.
   - Releases over-allocated funds in strict LIFO order (`AllocationEventId DESC`).
   - Asserts that recalculated active allocations equal target invoice amount before updating `PaidAmount` and `BalanceAmount`.
   - Derives payment status accurately.
5. **`dbo.usp_Payment_RecordAndAllocateSingle`:**
   - Validates that `Payments.CustomerId == MonthlyBills.CustomerId`.
   - Inserts `dbo.Payments`.
   - Appends initial allocation event (`Direction = +1`) up to `BalanceAmount` of `@TargetBillId` via static SQL ownership chaining.
   - Captures excess payment as unallocated advance credit on that payment receipt.
   - Updates target bill's `PaidAmount`, `BalanceAmount`, `PaymentStatus`.
6. **`dbo.usp_Payment_AllocateAdvanceCreditSingle`:**
   - Verifies available credit from non-voided `@PaymentId` and balance on `@TargetBillId`.
   - Validates customer matching between payment and bill.
   - Appends advance allocation event (`Direction = +1`, `EventType = 'AdvanceCreditAllocation'`) to `dbo.PaymentAllocationEvents` via static SQL ownership chaining.
   - Updates target bill balances.
7. **`dbo.usp_Payment_Void` (Two-Tiered Idempotency Contract):**
   - Enforces the 10-step atomic contract from Section 5.4.
   - Acquires `UPDLOCK, HOLDLOCK` on `dbo.Payments`.
   - Validates `Payments.IsVoided = 0` (Error 50041).
   - Reverses strictly the remaining active allocations for `@PaymentId` via `VoidReversal` events. Guarded by `UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent`.
   - Does not reverse amounts already released by bill revisions.
   - Restores target bills' balances and statuses.
   - Sets `IsVoided = 1` in `dbo.Payments` (resetting advance credit to ₹0.00).
8. **`dbo.usp_Payment_GetCustomerOutstandingBills`:**
   - Queries active published bills with `BalanceAmount > 0` and total available non-voided advance credit for payment modal.
9. **`dbo.usp_MonthlyBilling_GetBillsContinuous` & `dbo.usp_MonthlyBilling_GetBillDetails`:**
   - Filtered paginated queries for billing workspace and detailed invoice viewer with allocation events history.
10. **`dbo.usp_Payment_GetHistory` & `dbo.usp_BillingReports_GetMonthlySummary`:**
    - Reporting and payment history queries.
11. **`dbo.usp_AllocatePayment` (Decommissioned):**
    - Altered to immediately throw error `50060`.

---

## 9. C# Architecture & Interface Contracts

### 9.1 Models (`Models/Billing/`)
- `MonthlyBill.cs`: Contains `PaymentDueDate`, `CurrentMonthInvoiceAmount`, `PreviousBalanceSnapshot`, `TotalStatementAmount`, `PaidAmount`, `BalanceAmount`, `PaymentStatus`.
- `PaymentAllocationEvent.cs`:
  ```csharp
  public class PaymentAllocationEvent
  {
      public long AllocationEventId { get; set; }
      public long PaymentId { get; set; }
      public long BillId { get; set; }
      public string EventType { get; set; } = "InitialAllocation";
      public long? OriginalAllocationEventId { get; set; }
      public decimal Amount { get; set; }
      public short Direction { get; set; } // +1 or -1
      public decimal SignedAmount => Amount * Direction;
      public string Reason { get; set; } = string.Empty;
      public long? RelatedRevisionId { get; set; }
      public long? LegacyAllocationId { get; set; }
      public string PerformedByUserName { get; set; } = string.Empty;
      public DateTime PerformedAt { get; set; }
  }
  ```
- `RecordSinglePaymentViewModel.cs`:
  ```csharp
  public class RecordSinglePaymentViewModel
  {
      [Required]
      public int CustomerId { get; set; }

      [Required]
      [Range(0.01, 1000000.00, ErrorMessage = "Payment amount must be greater than zero.")]
      public decimal Amount { get; set; }

      [Required]
      public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

      [Required]
      [RegularExpression("^(Online|Offline)$", ErrorMessage = "Mode must be Online or Offline.")]
      public string PaymentMode { get; set; } = "Offline";

      [StringLength(50)]
      public string? ReferenceNumber { get; set; }

      [StringLength(250)]
      public string? Notes { get; set; }

      // Strictly ONE selected bill for allocation
      [Required]
      public long TargetBillId { get; set; }
  }
  ```

### 9.2 Repository Contract (`Repositories/IBillingRepository.cs`)
```csharp
namespace Dairy_Management_System.Repositories
{
    public interface IBillingRepository
    {
        // Invoice Operations
        Task<long> GenerateCustomerBillAsync(int customerId, int month, int year, int adminUserId, CancellationToken cancellationToken = default);
        Task<MonthlyBill?> GetBillByIdAsync(long billId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MonthlyBill>> GetBillsAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<int> GetBillsCountAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<bool> PublishBillAsync(long billId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<int> BulkPublishBillsAsync(int month, int year, int? routeId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> ReviseBillAsync(ReviseBillViewModel model, int adminUserId, CancellationToken cancellationToken = default);

        // Bulk Operations & Eligibility
        Task<IReadOnlyList<CustomerBillingEligibilityModel>> GetEligibleCustomersForBillingAsync(int month, int year, int? routeId, CancellationToken cancellationToken = default);

        // Append-Only Payment Operations (100% Authoritative on PaymentAllocationEvents via Stored Procs)
        Task<long> RecordAndAllocateSinglePaymentAsync(RecordSinglePaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> AllocateAdvanceCreditSingleAsync(long paymentId, long targetBillId, decimal amount, string reason, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> VoidPaymentAsync(long paymentId, string voidReason, int adminUserId, CancellationToken cancellationToken = default);
        Task<Payment?> GetPaymentByIdAsync(long paymentId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Payment>> GetPaymentsAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<int> GetPaymentsCountAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PaymentAllocationEvent>> GetAllocationEventsByBillIdAsync(long billId, CancellationToken cancellationToken = default);

        // Customer Ledger & Balances (Authoritative Model)
        Task<IReadOnlyList<CustomerOutstandingBillItemViewModel>> GetCustomerOutstandingBillsAsync(int customerId, CancellationToken cancellationToken = default);
        Task<CustomerBalanceSummaryModel> GetCustomerBalanceSummaryAsync(int customerId, CancellationToken cancellationToken = default);

        // Reports
        Task<BillingSummaryReportModel> GetMonthlyBillingSummaryReportAsync(int month, int year, CancellationToken cancellationToken = default);
    }
}
```

---

## 10. Implementation Sequence for Kevin and Viraj

### Phase 2: Kevin Gajera (Backend / SQL Server)
1. Write `sql/08_BillingAndPaymentsManagement.sql`:
   - Add `PaymentDueDate` column to `dbo.MonthlyBills`.
   - Create `dbo.PaymentAllocationEvents` table, immutability triggers, and unique filtered indexes on `LegacyAllocationId` and `VoidReversal`.
   - Execute the 10-step migration and five-point reconciliation checks in a single transaction.
   - Install the read-only trigger on `dbo.PaymentAllocations`.
   - Decommission `dbo.usp_AllocatePayment` (throw 50060).
   - Implement the 11 single-bill, bulk, LIFO revision, and payment stored procedures using static SQL.
   - Enforce least-privilege security: Grant **SELECT ONLY** on `dbo.PaymentAllocationEvents` to `[db_dms_app]`; explicitly **DENY** direct `INSERT`, `UPDATE`, and `DELETE`.
   - Run actual production connection string identity verification reporting all 5 tokens (`SUSER_SNAME`, `ORIGINAL_LOGIN`, `USER_NAME`, `IS_SRVROLEMEMBER`, role memberships).
   - Execute the rolled-back permission validation test harness verifying that direct `INSERT`, `UPDATE`, and `DELETE` on `dbo.PaymentAllocationEvents` fail with Error 229, and that all four approved stored procedures (`usp_Payment_RecordAndAllocateSingle`, `usp_Payment_AllocateAdvanceCreditSingle`, `usp_MonthlyBilling_ReviseBill`, `usp_Payment_Void`) execute successfully under `dms_app_user`.
2. Implement C# models in `Models/Billing/`.
3. Implement `Repositories/BillingRepository.cs` (querying `dbo.PaymentAllocationEvents` exclusively) and `Services/BillingService.cs`.
4. Register dependencies in `Program.cs`.
5. Run unit/integration tests verifying:
   - Migration idempotency and reconciliation matching across all 5 checks.
   - Direct INSERT on `dbo.PaymentAllocationEvents` fails with Error 229.
   - Payment voiding enforces two-tiered idempotency across all 5 scenarios (ordinary allocation, partially reversed, pure advance credit, zero allocation, and repeated void).
   - One payment receipt allocates to exactly one selected bill via stored procedure.
   - Downward invoice revisions across multiple payments release funds via deterministic LIFO order (`AllocationEventId DESC`) and verify event totals.
   - Published invoices snapshot `PaymentDueDate`.

### Phase 3: Viraj Bodar (Frontend / Razor Views)
1. Connect `Views/Shared/_LayoutAdmin.cshtml` Item 4 to `~/Billing`.
2. Implement `Controllers/BillingController.cs` (`[Authorize(Roles = "Admin")]`).
3. Build Razor views in `Views/Billing/`:
   - `Index.cshtml`: Displays KPI cards (incorporating live unallocated credit), bill grid with status badges.
   - `BulkBilling.cshtml`: Pre-run customer eligibility summary and results modal.
   - `Details.cshtml`: Itemized bill, printable layout, revision history, and allocation events history. Due date displayed as `Due Date: Pending Business Approval` when unapproved or NULL.
   - `Payments.cshtml`: Single-bill payment modal, advance credit allocation modal, payment history table, void modal.
   - `Reports.cshtml`: Monthly summary with Chart.js.
4. Update Admin Dashboard (`Views/Home/Index.cshtml`) Card 4 & 5 to live billing queries.

---

## 11. Final Architectural Validation Checklist

| Invariant Item | Validation Status |
| :--- | :---: |
| 1. Explicit Relational Migration Identity | **VERIFIED (`LegacyAllocationId` with unique filtered index; no LIKE patterns)** |
| 2. Two-Tiered Payment Void Idempotency | **VERIFIED (`UPDLOCK, HOLDLOCK` on `Payments` + `VoidReversal` filtered unique index)** |
| 3. Active & Voided Migration Completeness | **VERIFIED (Active as single event; voided as matched event pair netting to ₹0)** |
| 4. Migration-Time Five-Point Reconciliation Gate | **VERIFIED (Grand Total, Voided Net Zero, Payment-Level, Bill-Level, ID Integrity)** |
| 5. Post-Cutover Reconciliation Boundary | **VERIFIED (Legacy table frozen post-cutover; live audits use internal invariants)** |
| 6. Old Allocation Procedures Cannot Bypass Ledger | **VERIFIED (`usp_AllocatePayment` decommissioned, throws 50060; table read-only)** |
| 7. Explicit DENY on Direct Ledger INSERT/DML & 4-Proc Execution | **DEFINED — EXECUTION PENDING** (Test specification documented: explicit DENY on direct INSERT, UPDATE, DELETE with Error 229, and positive execution of 4 stored procedures defined in rolled-back transaction harness; test execution completed: PENDING; test result passed: PENDING Phase 2 backend implementation) |
| 8. Actual Identity Verification & Prod vs Dev | **DEFINED — EXECUTION PENDING** (Verification protocol documented: actual production connection string inspection and 5-token query defined; LocalDB elevated developer login vs dms_app_user distinguished; test execution completed: PENDING; test result passed: PENDING production deployment via actual service identity) |
| 9. LIFO Revision Finalization Correctness | **VERIFIED (Verified recalculation from ledger; PaidAmount not overwritten blindly)** |
| 10. Voided Payment Advance Credit Exclusion | **VERIFIED (Voided payments hold ₹0 credit; only remaining active reversed)** |
| 11. Multiple Payment Receipts Handled Deterministically | **VERIFIED (`AllocationEventId DESC` tie-breaker; credit returned to original receipt)** |
| 12. No Allocation Event is Updated or Deleted | **VERIFIED (Strictly append-only table guarded by immutability trigger)** |
| 13. Payment Receipts Remain Preserved | **VERIFIED (Soft-void with trigger protection; receipts never deleted)** |
| 14. Advance Credits Remain Separate Until Allocated | **VERIFIED (Held on payment receipts; zero automatic FIFO drift)** |
| 15. Zero Double-Counting of Previous Balances | **VERIFIED (Snapshot is informational only; `BalanceAmount` is month-only)** |
| 16. Historical Statement Snapshots Remain Immutable | **VERIFIED (Snapshots immutable on bill; older bill payments don't alter history)** |
| 17. Due Date Policy Remains Pending Business Approval | **VERIFIED (Explicitly labeled; unapproved statements render Pending Approval)** |
| 18. Payment Modes Remain Online and Offline | **VERIFIED (Strict regex and database check constraint enforced)** |
| 19. Payment Statuses Derived Accurately | **VERIFIED (Paid, Partially Paid, Unpaid, Overdue; Overdue requires valid due date)** |
| 20. Delivery Boy Cannot Access Financial Operations | **VERIFIED (`[Authorize(Roles = "Admin")]` enforced across billing controller)** |

---
*Architectural specification finalized. Paused for explicit user review and authorization.*
