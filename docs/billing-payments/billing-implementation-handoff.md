# DAY 5 — BILLING & PAYMENTS IMPLEMENTATION HANDOFF
**Dairy Management System (DMS)**  
**Document Identifier:** `DMS-HANDOFF-DAY5-BILLING-009` (Actual Login Verification & Full Permission Test Harness Revision)  
**Author:** Rachit Parsana (Lead Architect)  
**Assigned Engineers:** Kevin Gajera (Backend / SQL Server), Viraj Bodar (Frontend / Razor)  
**Git Baseline:** `53bd84ec7dcdde755e522d5c39fccdc9119e0433`  
**Governing Document:** `docs/billing-payments/billing-and-payments-architecture.md` (v009)

---

## 1. Objective and Team Workflow

This handoff document establishes the strict boundary of responsibilities, technical contracts, and execution sequence for the Day 5 Billing & Payments Management module. It fully incorporates the actual application security identity verification via the actual production connection string, development vs. production identity distinction, complete three-operation negative permission testing (INSERT, UPDATE, DELETE blocked with Error 229), positive execution testing for all four approved stored procedures under a rolled-back transaction harness, two-tiered payment void idempotency (`UPDLOCK, HOLDLOCK` on `dbo.Payments` and filtered unique index on `VoidReversal`), the five-point migration-time reconciliation boundary, explicit relational migration identity (`LegacyAllocationId`), static SQL ownership chaining, corrected LIFO bill revision finalization, authoritative voided payment credit rules, customer statement presentation standards, and the due date implementation contract.

```
       [Rachit] — Phase 1: Architecture & Technical Contracts (Complete)
          │
          ▼
       [Kevin]  — Phase 2: Database Migration & ADO.NET Backend
          │        • sql/08_BillingAndPaymentsManagement.sql
          │        • Explicit LegacyAllocationId with Unique Filtered Index
          │        • Secondary Filtered Unique Index on VoidReversal
          │        • Single Transaction Migration (Active + Voided Pairs)
          │        • Migration-Time Five-Point Automated Reconciliation Gate
          │        • Actual Connection String & 5-Token Identity Verification
          │        • Complete 3-Op Negative Tests (INSERT/UPDATE/DELETE Error 229)
          │        • Positive 4-Proc Execution Harness (Rolled-Back Transaction)
          │        • Two-Tiered Payment Void Idempotency (5 Scenarios Tested)
          │        • Decommission dbo.usp_AllocatePayment (Error 50060)
          │        • Read-Only Trigger on dbo.PaymentAllocations
          │        • Models/Billing/* (including LegacyAllocationId)
          │        • Repositories/BillingRepository.cs (100% on Events Ledger)
          │        • Services/BillingService.cs
          │        • Verification Report & Git Push
          │
          ▼
       [Viraj]  — Phase 3: Razor Views, Controllers & Dashboard Integration
                   • Controllers/BillingController.cs
                   • Views/Billing/* (Statement presentation, LIFO event breakdown)
                   • Views/Home/Index.cshtml live card updates
                   • Verification Report & Git Push
```

---

## 2. Kevin Gajera — Backend & SQL Server Responsibilities

Kevin owns the database schema, migration scripts, reconciliation scripts, stored procedures, data access layer, domain services, and transaction boundaries.

### 2.1 Database Deliverables (`sql/08_BillingAndPaymentsManagement.sql`)

1. **Schema Enhancements & Uniqueness Safeguards:**
   - **`dbo.MonthlyBills` Column Addition:** Add nullable metadata column `PaymentDueDate DATE NULL` (permanently snapshotted upon publication).
   - **`dbo.PaymentAllocationEvents` Table Creation (Strictly Append-Only Authoritative Ledger):**
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
         [LegacyAllocationId] BIGINT NULL,  -- Explicit relational migration key (NULL for runtime events)
         [PerformedBy] INT NOT NULL CONSTRAINT [FK_PaymentAllocationEvents_PerformedBy] REFERENCES [dbo].[Users]([UserId]),
         [PerformedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAllocationEvents_PerformedAt] DEFAULT (SYSUTCDATETIME())
     );

     -- 1. Enforce 1:1 legacy migration uniqueness via filtered index
     CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_LegacyAllocationId]
     ON [dbo].[PaymentAllocationEvents]([LegacyAllocationId])
     WHERE [LegacyAllocationId] IS NOT NULL;

     -- 2. Secondary defense: Enforce 1:1 void reversal uniqueness per original event
     CREATE UNIQUE NONCLUSTERED INDEX [UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent]
     ON [dbo].[PaymentAllocationEvents]([OriginalAllocationEventId])
     WHERE [EventType] = 'VoidReversal' AND [OriginalAllocationEventId] IS NOT NULL;
     ```
   - **Financial Immutability Triggers:**
     - `trg_MonthlyBills_PreventDirectDelete`: Blocks `DELETE` on published bills (`IsPublished = 1`).
     - `trg_MonthlyBillRevisions_PreventModification`: Blocks `UPDATE` / `DELETE` on revision history.
     - `trg_Payments_PreventDirectDelete`: Blocks `DELETE` on payments (forces `usp_Payment_Void`).
     - `trg_PaymentAllocationEvents_PreventModification`: Blocks `UPDATE` / `DELETE` on allocation events.
     - `trg_PaymentAllocations_LegacyReadOnly`: Blocks `INSERT`, `UPDATE`, and `DELETE` on archived `dbo.PaymentAllocations`.

2. **Single Transaction Migration (Active + Voided Pairs):**
   - The entire migration executes within a single transaction with `SET XACT_ABORT ON; BEGIN TRANSACTION; ... COMMIT TRANSACTION;`.
   - **Active rows (`IsVoided = 0`):** Migrated as single `InitialAllocation` events with `LegacyAllocationId = pa.AllocationId`, `Direction = 1`.
   - **Historical voided rows (`IsVoided = 1`):** Migrated as an atomic pair:
     1. `InitialAllocation` event with `LegacyAllocationId = pa.AllocationId`, `Direction = 1`.
     2. Linked `VoidReversal` event with `OriginalAllocationEventId = <initialEventId>`, `LegacyAllocationId = NULL`, `Direction = -1`. Guarded by `UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent`.
     - *Net active allocation = ₹0.00.*
   - Idempotency guard: `NOT EXISTS (SELECT 1 FROM dbo.PaymentAllocationEvents pae WHERE pae.LegacyAllocationId = pa.AllocationId)`.

3. **Migration-Time Five-Point Automated Reconciliation Gate:**
   Kevin must execute the following five reconciliation checks immediately after migration and before committing/switching:
   - **Check 1 (Active Legacy Grand Total & Attribute Matching):** Compares only `pa.IsVoided = 0` joined on `pae.LegacyAllocationId = pa.AllocationId`. Verifies matching `PaymentId`, `BillId`, `Amount`, `Direction = 1`, and identical grand totals.
   - **Check 2 (Voided Allocations Event Pair & Net Zero):** Verifies each `pa.IsVoided = 1` row has exactly one initial event and one reversal event, with matching attributes and net signed amount of exactly ₹0.00.
   - **Check 3 (Payment-Level Reconciliation):** Aggregates the complete migrated population (both positive initial events and negative void reversals). Compares active allocation totals by `PaymentId` against legacy non-voided allocations. Voided pairs contribute zero.
   - **Check 4 (Bill-Level Reconciliation):** Aggregates complete migrated population grouped by `BillId`. Compares active net totals by `BillId` against legacy non-voided allocations. Voided pairs contribute zero.
   - **Check 5 (Identifier and Relational Integrity):** Verifies zero missing `LegacyAllocationId`, zero duplicate `LegacyAllocationId`, zero missing initial events for voided allocations, zero missing/duplicated reversal events, zero orphan reversals, and zero payment/bill identity mismatches.
   - **Rollback Gate:** If any check fails, trigger immediate `ROLLBACK TRANSACTION`. Do not switch application reads. Legacy data remains intact. Never delete or rewrite historical data to force parity.
   - **Post-Cutover Boundary:** These five checks are strictly migration-time checks. Post-cutover operational health is audited against the event ledger's own internal mathematical invariants, NOT against the frozen legacy table.

4. **Actual Identity Verification & Production Least-Privilege Contract [Status: DEFINED — EXECUTION PENDING]:**
   - **Actual Production Application Connection String Verification Protocol:**
     - Pre-deployment security verification must be executed through a connection established using the **actual production application connection string and service identity** configured in `appsettings.Production.json` (or cloud environment secret store).
     - **Prohibition on Deployment Connection Claims:** Engineers must **never claim** that executing verification queries through an administrator's deployment connection (e.g. logging in as `sa` or Windows Administrator and using `EXECUTE AS USER = 'dms_app_user'`) proves application least privilege. Admin sessions hold elevated server-level tokens and bypass routes that do not reflect the physical connection pool context. Production compliance sign-off requires connecting under the actual application credentials.
   - **Dev vs. Prod Distinction:**
     - LocalDB development uses Windows Authentication with elevated developer privileges (sysadmin/dbo) which bypasses `DENY`. Kevin must never claim LocalDB's elevated developer login proves production least privilege.
     - To test the security boundary in LocalDB development, Kevin must explicitly distinguish the developer login from the application principal by either using `EXECUTE AS USER = 'dms_app_user'` in local sessions or connecting via a dedicated restricted test login.
     - In production, connection pools authenticate strictly under a dedicated, non-sysadmin service login mapped to database user `dms_app_user` with membership only in `[db_dms_app]` and `[public]`. Confirm `IS_SRVROLEMEMBER('sysadmin') = 0` and `IS_ROLEMEMBER('db_owner') = 0`.
   - **Table Permissions & Explicit DENY:**
     - Grant **`SELECT` ONLY** on `dbo.PaymentAllocationEvents` to `[db_dms_app]`.
     - Issue explicit **`DENY INSERT, UPDATE, DELETE ON dbo.PaymentAllocationEvents TO [db_dms_app], [dms_app_user];`** to guarantee direct writes are blocked regardless of inherited roles.
   - **Static SQL Ownership Chaining:** All stored procedures must use static SQL only. Dynamic SQL is strictly banned because it breaks ownership chaining and triggers Error 229.
   - **Actual Identity & Five-Token Verification Query (Run via Actual Connection):**
     > [!IMPORTANT]
     > **Actual Identity Verification Status:** `DEFINED — EXECUTION PENDING`  
     > - **Test Specification Documented:** COMPLETE (Connection protocol, 5-token query, and role assertions fully specified).  
     > - **Test Execution Completed:** PENDING (Must be executed through the real production connection string and service identity).  
     > - **Test Result Passed:** PENDING (Awaiting production deployment).  
     > - **Development Boundary:** LocalDB development verification under an elevated developer Windows login must NOT be represented as production verification.

     ```sql
     -- 1. Identity & Non-Sysadmin Check
     SELECT 
         SUSER_SNAME() AS CurrentLogin,
         ORIGINAL_LOGIN() AS OriginalLogin,
         USER_NAME() AS CurrentDatabaseUser,
         IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME()) AS IsSysadmin;
     -- Production Gate: IsSysadmin MUST be 0; CurrentLogin must be dedicated service login.

     -- 2. Role Memberships of Mapped User
     SELECT 
         u.name AS DatabaseUser,
         r.name AS DatabaseRole,
         IS_ROLEMEMBER('db_owner') AS IsDbOwner,
         IS_ROLEMEMBER('db_datawriter') AS IsDbDatawriter
     FROM sys.database_role_members rm
     JOIN sys.database_principals u ON rm.member_principal_id = u.principal_id
     JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
     WHERE u.name = USER_NAME();
     -- Production Gate: DatabaseRole must be 'db_dms_app' only; IsDbOwner = 0; IsDbDatawriter = 0.

     -- 3. Effective Permissions via HAS_PERMS_BY_NAME
     SELECT 
         HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'SELECT') AS CanSelect,
         HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'INSERT') AS CanInsert,
         HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'UPDATE') AS CanUpdate,
         HAS_PERMS_BY_NAME(N'dbo.PaymentAllocationEvents', N'OBJECT', N'DELETE') AS CanDelete,
         HAS_PERMS_BY_NAME(N'dbo.usp_Payment_RecordAndAllocateSingle', N'OBJECT', N'EXECUTE') AS CanExecuteRecordPayment;
     -- Production Gate: CanSelect = 1; CanInsert = 0; CanUpdate = 0; CanDelete = 0; CanExecute = 1.
     ```
   - **Complete Rolled-Back Security Validation Test Harness:**
     > [!IMPORTANT]
     > **Permission Test Harness Status:** `DEFINED — EXECUTION PENDING`  
     > - **Test Specification Documented:** COMPLETE (All 3 direct DML denial tests [INSERT, UPDATE, DELETE Error 229] and positive execution tests for all 4 approved stored procedures are fully specified in Section 2.2 #7 of architecture spec).  
     > - **Test Execution Completed:** PENDING (Awaiting execution by Kevin in Phase 2 backend implementation).  
     > - **Test Result Passed:** PENDING (To be validated by Kevin during Phase 2 execution).  
     > - **Isolation Guarantee:** Runs inside a rolled-back transaction so real financial records are never modified.

     Kevin must execute the complete test harness under `dms_app_user` within an explicit transaction that is 100% rolled back, proving:
     1. Direct `INSERT` on `dbo.PaymentAllocationEvents` fails with Error 229.
     2. Direct `UPDATE` on `dbo.PaymentAllocationEvents` fails with Error 229.
     3. Direct `DELETE` on `dbo.PaymentAllocationEvents` fails with Error 229.
     4. Positive execution succeeds for all four stored procedures (`usp_Payment_RecordAndAllocateSingle`, `usp_Payment_AllocateAdvanceCreditSingle`, `usp_MonthlyBilling_ReviseBill`, `usp_Payment_Void`) using controlled test data.
     5. Transaction executes `ROLLBACK TRANSACTION;` ensuring zero persistent test records and zero modifications to real financial data.
   - Decommission `dbo.usp_AllocatePayment` to throw error `50060`. Revoke legacy write permissions on `dbo.PaymentAllocations`.

5. **Core Stored Procedures:**
   - **`dbo.usp_MonthlyBilling_GenerateCustomerBill`:**
     - Calculates milk totals from `dbo.DailyMilkDeliveries` & `DailyMilkDeliveryItems` (`Status = 'Completed'`).
     - Enforces the `NOT EXISTS` guard on approved vacations (`dbo.CustomerVacations`).
     - Queries `PreviousBalanceSnapshot = ISNULL(SUM(BalanceAmount), 0.00)` from prior published bills (informational only!).
     - Sets `CurrentMonthInvoiceAmount = MilkAmount - Adjustments`.
     - Sets `TotalStatementAmount = PreviousBalanceSnapshot + CurrentMonthInvoiceAmount`.
     - Sets `BalanceAmount = CurrentMonthInvoiceAmount - PaidAmount`. *(Never adds previous balance into BalanceAmount!)*
     - Upserts `dbo.MonthlyBills` (Draft state) and itemizes lines into `dbo.MonthlyBillItems`.
   - **`dbo.usp_MonthlyBilling_GenerateBulk`:**
     - Accepts `@BillingMonth`, `@BillingYear`, `@RouteId` (optional), `@AdminUserId`.
     - Acquires application lock `sp_getapplock @Resource = 'DMS_BulkBilling_Lock'`.
     - Loops through eligible active customers with isolated transactions per customer.
     - Returns summary result set (Total, Succeeded, Skipped, Failed).
   - **`dbo.usp_MonthlyBilling_PublishBill` & `usp_MonthlyBilling_BulkPublish`:**
     - Checks due date policy status or accepts `@GraceDays`. If unapproved and omitted, returns error `50070`.
     - Sets `IsPublished = 1`, `PublishedAt = SYSUTCDATETIME()`.
     - Permanently snapshots `PaymentDueDate = DATEADD(day, @GraceDays, PeriodEndDate)`.
     - Assigns initial status (`Paid` if balance is 0, `Unpaid` if within due date, `Overdue` if past due date).
   - **`dbo.usp_MonthlyBilling_ReviseBill` (Corrected LIFO Reversal & Balance Finalization):**
     - Accepts `@BillId`, `@ExpectedVersion`, `@Adjustments`, `@Reason`, `@AdminUserId`.
     - Locks target bill using `UPDLOCK, HOLDLOCK` and verifies optimistic concurrency (`Version = @ExpectedVersion`).
     - Inserts historical audit record into `dbo.MonthlyBillRevisions`.
     - Calculates `TargetInvoiceAmount = MilkAmount - Adjustments`.
     - Calculates `CurrentActiveAllocation = SUM(SignedAmount)` from `dbo.PaymentAllocationEvents`.
     - **Evaluation:**
       - If `TargetInvoiceAmount > CurrentActiveAllocation`: No reversal. Final `PaidAmount = CurrentActiveAllocation`, Final `BalanceAmount = TargetInvoiceAmount - CurrentActiveAllocation`.
       - If `TargetInvoiceAmount == CurrentActiveAllocation`: No reversal. Final `PaidAmount = CurrentActiveAllocation`, Final `BalanceAmount = 0.00`.
       - If `TargetInvoiceAmount < CurrentActiveAllocation`: Executes deterministic LIFO reversal across candidate events ordered by `AllocationEventId DESC`. Reversal events appended with `Direction = -1`, linked to `OriginalAllocationEventId`. Recalculates active allocation from ledger and asserts parity with `TargetInvoiceAmount` before proceeding. Final `PaidAmount = RecalculatedActiveAllocation`, Final `BalanceAmount = 0.00`.
     - Derives `PaymentStatus`: `Paid` if balance = 0; `Unpaid` if paid = 0; `Partially Paid` if paid > 0; `Overdue` strictly when `PaymentDueDate IS NOT NULL` and expired.
     - Atomically updates `dbo.MonthlyBills` and increments `Version = Version + 1`.
   - **`dbo.usp_Payment_RecordAndAllocateSingle` (Strict One-Bill Contract):**
     - Validates `Payments.CustomerId == MonthlyBills.CustomerId`.
     - Accepts `@CustomerId`, `@Amount`, `@PaymentDate`, `@PaymentMode`, `@ReferenceNumber`, `@Notes`, `@TargetBillId`, `@AdminUserId`.
     - Inserts into `dbo.Payments`.
     - Appends initial allocation event (`Direction = +1`, `EventType = 'InitialAllocation'`) up to `BalanceAmount` of `@TargetBillId` via static SQL ownership chaining. Excess stays as unallocated advance credit on that payment receipt.
     - Updates target bill's `PaidAmount`, `BalanceAmount`, `PaymentStatus`.
   - **`dbo.usp_Payment_AllocateAdvanceCreditSingle`:**
     - Accepts `@PaymentId`, `@TargetBillId`, `@AmountToAllocate`, `@Reason`, `@AdminUserId`.
     - Validates available credit on non-voided `@PaymentId` and balance on `@TargetBillId`.
     - Validates customer matching between payment and target bill.
     - Appends advance allocation event (`Direction = +1`, `EventType = 'AdvanceCreditAllocation'`) to `dbo.PaymentAllocationEvents` via static SQL ownership chaining.
     - Updates target bill balances.
   - **`dbo.usp_Payment_Void` (Two-Tiered Idempotency & 10-Step Contract):**
     - Accepts `@PaymentId`, `@VoidReason` ($\ge 10$ chars), `@AdminUserId`.
     - Step 1: `BEGIN TRANSACTION` with `SET XACT_ABORT ON`.
     - Step 2: Concurrency lock: `SELECT CustomerId, Amount, IsVoided FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK) WHERE PaymentId = @PaymentId`.
     - Step 3: Existence verification (throw Error 50040 if not found).
     - Step 4: Tier 1 Primary Idempotency Check: `IF @IsVoided = 1 THROW 50041, 'Payment is already voided. Repeated void operations are strictly prohibited.', 1;`. (Catches duplicate void calls across all payment profiles!).
     - Step 5: Queries remaining active allocations for `@PaymentId` from event ledger.
     - Step 6: Appends `VoidReversal` events (`Direction = -1`) strictly for remaining active allocations. Guarded at storage level by Tier 2 filtered unique index `UQ_PaymentAllocationEvents_VoidReversal_OriginalEvent`.
     - Step 7: Does NOT reverse amounts already released by prior bill revisions.
     - Step 8: Updates `Payments.IsVoided = 1`, `VoidedAt = SYSUTCDATETIME()`, `VoidReason = @VoidReason` within the same transaction.
     - Step 9: Recalculates affected bills' `PaidAmount`, `BalanceAmount`, and `PaymentStatus`.
     - Step 10: `COMMIT TRANSACTION` atomically.
     - Handles all 5 worked scenarios: ordinary bill allocations, partially reversed allocations, pure advance credit, zero allocations, and repeated void requests.
   - **`dbo.usp_Payment_GetCustomerOutstandingBills`:**
     - Returns active published bills with `BalanceAmount > 0` and total available non-voided advance credit.
   - **`dbo.usp_MonthlyBilling_GetBillsContinuous` & `dbo.usp_MonthlyBilling_GetBillDetails`:**
     - Filtered paginated queries for billing workspace and detailed invoice viewer with allocation events history.
   - **`dbo.usp_Payment_GetHistory` & `dbo.usp_BillingReports_GetMonthlySummary`:**
     - Reporting queries for financial dashboard and ledger history.

### 2.2 C# Backend Deliverables
1. **Models (`Models/Billing/`):**
   - `MonthlyBill.cs`, `MonthlyBillItem.cs`, `MonthlyBillRevision.cs`
   - `Payment.cs`
   - `PaymentAllocationEvent.cs` (Includes `public long? LegacyAllocationId { get; set; }`)
   - `RecordSinglePaymentViewModel.cs`, `ReviseBillViewModel.cs`, `BulkBillingResultViewModel.cs`
2. **Repository (`Repositories/IBillingRepository.cs` & `BillingRepository.cs`):**
   - Implements ADO.NET data access using `ISqlConnectionFactory`.
   - Queries `dbo.PaymentAllocationEvents` exclusively. Does **not** query `dbo.PaymentAllocations`.
   - Direct INSERT/UPDATE/DELETE on `PaymentAllocationEvents` are never issued by C# code; all ledger operations invoke approved stored procedures.
3. **Service (`Services/IBillingService.cs` & `BillingService.cs`):**
   - Enforces business validations, logger calls, and error code mapping.
4. **Registration (`Program.cs`):**
   - Register `IBillingRepository` &rarr; `BillingRepository` (Scoped).
   - Register `IBillingService` &rarr; `BillingService` (Scoped).

---

## 3. Viraj Bodar — Frontend & UI Responsibilities

Viraj owns the user experience, Razor views, CoreUI integration, JavaScript calculation engines, modal workflows, and responsive UI components.

### 3.1 Controller Deliverables (`Controllers/BillingController.cs`)
- Enforce `[Authorize(Roles = "Admin")]` on the entire controller.
- Implement actions for:
  - `Index` (Monthly billing workspace with KPI cards and paginated bill listing)
  - `BulkBilling` (Bulk billing runner with eligibility review)
  - `GenerateBulk` (POST endpoint executing bulk run)
  - `GenerateSingle` (POST endpoint for single-customer draft)
  - `Publish` & `BulkPublish` (POST endpoints passing explicit grace parameters)
  - `Details` (Itemized invoice statement with printable layout)
  - `Revise` (POST endpoint for bill revision modal)
  - `GetCustomerDues` (AJAX endpoint for payment allocation modal)
  - `RecordPayment` (POST endpoint for recording payment to selected bill)
  - `AllocateAdvanceCredit` (POST endpoint for applying unallocated credit to bill)
  - `VoidPayment` (POST endpoint for voiding payment)
  - `Payments` (Payment history log)
  - `Reports` (Monthly summary reports with Chart.js analytics)

### 3.2 Razor View Deliverables (`Views/Billing/`)
1. **`Views/Billing/Index.cshtml`:**
   - 4 Top KPI cards: Total Billed Amount (₹), Total Litres Invoiced, Total Collections (₹), Outstanding Dues (₹).
   - Filter bar: Month, Year, Route, Payment Status (`Unpaid`, `Partially Paid`, `Paid`, `Overdue`), Search.
   - Action buttons: "Run Bulk Billing", "Bulk Publish", "Record Payment".
   - Data table displaying `BillNumber`, `CustomerName`, `Route`, `Litres`, `InvoiceAmount`, `PaidAmount`, `BalanceAmount`, `PaymentStatus` badge, and Action dropdown.
2. **`Views/Billing/BulkBilling.cshtml`:**
   - Pre-run customer eligibility summary table (Eligible count, Zero consumption count, Already published count).
   - "Execute Bulk Billing" confirmation modal.
   - Results modal displaying Succeeded, Skipped, and Failed customers.
3. **`Views/Billing/Details.cshtml` (Statement Presentation Layout):**
   - Professional invoice layout optimized for both screen viewing and printing (`@media print`).
   - **Due Date Display Rule:**
     - If `PaymentDueDate` is set and policy is approved: renders `PaymentDueDate: dd-MMM-yyyy`.
     - If unapproved or null: renders `Due Date: Pending Business Approval` with footnote `(Proposed default: 10th of following month, subject to formal administrative policy approval)`.
   - Itemized table with category, delivered days, volume, unit price snapshot, and subtotal.
   - **Financial Statement Breakdown (5 Separate Lines):**
     1. Previous Outstanding: ₹X.XX
     2. Current Month Charges: ₹X.XX
     3. Total Statement Amount Due: ₹X.XX
     4. Available Unallocated Advance Credit: ₹X.XX *(Displayed separately! Never deducted from individual invoice balance!)*
     5. Net Customer Account Outstanding: ₹X.XX
   - Tab 1: Payment Allocation Events History (Event #, Date, Mode, Event Type, Directional Signed Amount, Reason).
   - Tab 2: Bill Revision History.
4. **`Views/Billing/Payments.cshtml`:**
   - "Record Payment" modal with dynamic customer search:
     - Select Customer &rarr; AJAX fetches active outstanding bills and unallocated advance credit.
     - Single-bill selection: Admin selects the exact bill to settle.
     - If payment $>$ bill balance, UI clearly indicates: "₹X.XX will settle this bill in full; remaining ₹Y.YY will be retained as unallocated advance credit on this customer's account."
   - Payment history table with void modal requiring 10+ character justification.
5. **`Views/Billing/Reports.cshtml`:**
   - Monthly billing vs collection trends using Chart.js.
   - Route-wise collection breakdown and CSV exports.

### 3.3 Navigation & Dashboard Integration
1. **Admin Sidebar (`Views/Shared/_LayoutAdmin.cshtml`):**
   - Connect Item #4 ("Monthly Billing Management") to `href="~/Billing"`.
   - Maintain exactly 10 items in emerald theme.
2. **Admin Dashboard (`Views/Home/Index.cshtml`):**
   - Connect Card 4 ("Monthly Billing Total") to live monthly billing total.
   - Connect Card 5 ("Pending Payments") to live outstanding balance.
   - Connect fast action link to `~/Billing`.

---

## 4. Acceptance Criteria

| Criteria ID | Category | Requirement | Validation Method |
| :--- | :--- | :--- | :--- |
| **AC-01** | Explicit Migration Key & Void Uniqueness | `LegacyAllocationId` and `VoidReversal` filtered unique indexes physically enforce 1:1 migration and void uniqueness. | Query schema for columns and both filtered indexes |
| **AC-02** | Complete Migration | Both active (`IsVoided = 0`) and historical voided (`IsVoided = 1`) legacy rows migrate with full event linearity. | Verify voided rows have matched pairs netting to ₹0.00 |
| **AC-03** | Five-Point Reconciliation Gate | Five automated migration-time checks report zero discrepancies, zero duplicated IDs, and zero missing legacy IDs before cutover. | Run Checks 1–5; verify PASSED status on all 5 |
| **AC-04** | Legacy Decommissioning | `dbo.usp_AllocatePayment` throws 50060 and writes to `dbo.PaymentAllocations` are blocked by trigger. | Direct EXECUTE and INSERT fail with expected errors |
| **AC-05** | Correct LIFO Reversal | Downward revision reverses candidate allocations in LIFO order (`AllocationEventId DESC`) and verifies ledger totals. | Revise ₹1000 bill (paid by ₹400, ₹350, ₹250) to ₹800; verify P3 reversed by ₹200 |
| **AC-06** | Accurate Status Derivation | Procedural finalization derives Paid, Partially Paid, Unpaid, and Overdue without overwriting PaidAmount blindly. | Revise ₹1000 bill (paid ₹700) to ₹1200; verify PaidAmount stays ₹700 and balance is ₹500 |
| **AC-07** | Two-Tiered Void Idempotency | `usp_Payment_Void` locks row with `UPDLOCK, HOLDLOCK`, checks `IsVoided = 0`, reverses active allocations, resets credit to ₹0, and rejects repeated voids across all 5 scenarios with Error 50041. | Test all 5 void scenarios; verify repeated void throws 50041 |
| **AC-08** | Single-Bill Allocation | One payment receipt allocates to exactly one selected invoice. Excess remains as unallocated advance credit. | Record payment > bill balance; verify credit retained |
| **AC-09** | Statement Presentation | Advance credit is shown separately on customer statements and not deducted from individual bill balances. | Inspect statement view model and rendered HTML |
| **AC-10** | Double-Counting Proof | Summing `MonthlyBills.BalanceAmount` across all bills yields exact debt without double-counting previous balances. | Run 3-month test sequence; verify total dues = ₹700 |
| **AC-11** | Old Bill Payment | Paying an old bill updates that bill's balance to 0 and leaves newer bills untouched. | Pay Jan bill in March; verify Feb bill balance unchanged |
| **AC-12** | Vacation Guard | Deliveries overlapping approved vacations contribute ₹0.00 and 0.000 L to the bill. | Billing query verification with approved vacation |
| **AC-13** | Rate Determinism | Invoices use `DailyMilkDeliveryItems.UnitPriceSnapshot`, not current rates. | Category price updated; bill generates with historical price |
| **AC-14** | Snapshotted Due Date | `PaymentDueDate` is written to `dbo.MonthlyBills` on publication and remains immutable. Unapproved statements show Pending Approval. | Verify column populated on published bill; check statement text |
| **AC-15** | Payment Modes | Strictly `Online` and `Offline` supported. | Invalid mode rejected by check constraint |
| **AC-16** | Bulk Billing Lock | Simultaneous bulk billing runs blocked via `sp_getapplock`. | Concurrency test with overlapping bulk requests |
| **AC-17** | Defense-in-Depth Security & Actual Identity Verification | Application role `db_dms_app` has SELECT ONLY on `PaymentAllocationEvents` with explicit DENY on INSERT, UPDATE, and DELETE. Actual production connection string reports non-sysadmin (0), non-db_owner (0), and role `db_dms_app`. Procs use static SQL ownership chaining. | Under `dms_app_user` within rolled-back transaction: all 3 direct DML ops (INSERT, UPDATE, DELETE) fail with Error 229; all 4 stored procedures (`usp_Payment_RecordAndAllocateSingle`, `usp_Payment_AllocateAdvanceCreditSingle`, `usp_MonthlyBilling_ReviseBill`, `usp_Payment_Void`) execute successfully. |
| **AC-18** | UI Invariants | Admin sidebar maintains exactly 10 items in emerald theme. Item 4 connects to `~/Billing`. | Layout and navigation verification |
| **AC-19** | Non-Incremental Build | `dotnet build --no-incremental` builds with 0 warnings and 0 errors. | Command execution check |

---
*Implementation handoff specification finalized. Awaiting review and explicit authorization before Phase 2 implementation begins.*
