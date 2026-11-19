# DAY 6 — DELIVERY BOY & COMPENSATION MANAGEMENT IMPLEMENTATION HANDOFF
**Dairy Management System (DMS)**  
**Document Identifier:** `DMS-HANDOFF-DAY6-DELIVERYBOY-COMPENSATION-002` (Final Approved Handoff Revision)  
**Author:** Lead Solution Architect & Senior Business Analyst (Rachit Parsana Architecture Team)  
**Recipients:** Kevin Gajera (Backend Engineer), Viraj Bodar (Frontend Engineer)  
**Approved Baseline Git Commit:** `d3bcd0ea8193bc87fde287067e88570f7b032510`  
*(Incorporates Kevin's Day 5 due-date fix `3dbe522bb8642999598d69e3cd7e8025e0bcff9c` and route lookup service `8a531bcb565d0b788e39f3e720bfa12b8fc7aa8a`)*  
**Date:** November 20, 2026  

---

## 1. Executive Summary & Implementation Sequence

This handoff document provides Kevin Gajera (Backend) and Viraj Bodar (Frontend) with the authoritative technical specification to implement Day 6 without inventing business rules, data models, or SQL procedures.

The implementation follows our strict two-phase engineering sequence:
1. **Phase 2 — Backend Implementation (Kevin Gajera):**
   - Author and deploy idempotent SQL migration script `sql/10_DeliveryBoyAndCompensationManagement.sql`.
   - Implement dedicated adjustment ledger `dbo.DeliveryBoyCompensationAdjustments`.
   - Implement domain models, DTOs, and request ViewModels in `Models/DeliveryBoy/`.
   - Implement ADO.NET repositories: `IDeliveryBoyRepository`, `DeliveryBoyRepository`, `ICompensationRepository`, `CompensationRepository`.
   - Implement application services: `IDeliveryBoyService`, `DeliveryBoyService`, `ICompensationService`, `CompensationService`.
   - Update `RouteClassifier` in `Program.cs` to resolve the `/DeliveryBoy` path collision.
   - Register dependencies cleanly in `Program.cs`.
   - Author and execute automated backend verification test harness `scratch/Day6BackendVerification`.
   - Await Lead Architect review and commit authorization.
2. **Phase 3 — Frontend Implementation (Viraj Bodar):**
   - Implement `Controllers/DeliveryBoyController.cs` under `[Authorize(Roles = "Admin")]`.
   - Author 6 dedicated Razor views in `Views/DeliveryBoy/` (`Index.cshtml`, `Create.cshtml`, `Details.cshtml`, `Edit.cshtml`, `Compensation.cshtml`, `CompensationDetails.cshtml`).
   - Integrate with Admin layout sidebar (`Views/Shared/_LayoutAdmin.cshtml`, Item 2 active link `~/DeliveryBoy`).
   - Author and execute automated frontend verification test harness `scratch/Day6FrontendVerification`.
   - Await Lead Architect review and commit authorization.

---

## 2. Database & SQL Migration Plan (Kevin)

### 2.1 SQL Script Specification
- **File Name:** `sql/10_DeliveryBoyAndCompensationManagement.sql`
- **Conventions:** Follow `sql/08_BillingAndPaymentsManagement.sql` and `sql/09_RouteLookup.sql`.
- **Target Database Execution Safety:**
  - Idempotent execution (`CREATE OR ALTER PROCEDURE`, `IF NOT EXISTS`).
  - Precondition checks: Abort immediately if database is a system database (`master`, `tempdb`, `model`, `msdb`).
  - Check for application role: Verify `db_dms_app` exists; abort safely if missing without leaving SSMS session in `NOEXEC` mode.
  - Grants: Grant `EXECUTE` on all newly created stored procedures strictly to `db_dms_app`.

### 2.2 Table Creation & Least-Privilege Permissions: `dbo.DeliveryBoyCompensationAdjustments`
```sql
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DeliveryBoyCompensationAdjustments')
BEGIN
    PRINT 'Creating table DeliveryBoyCompensationAdjustments...';
    CREATE TABLE [dbo].[DeliveryBoyCompensationAdjustments] (
        [AdjustmentId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DeliveryBoyCompensationAdjustments] PRIMARY KEY CLUSTERED,
        [CompensationId] BIGINT NOT NULL CONSTRAINT [FK_DeliveryBoyAdjustments_CompensationId] REFERENCES [dbo].[DeliveryBoyMonthlyCompensations]([CompensationId]),
        [AdjustmentType] NVARCHAR(30) NOT NULL CONSTRAINT [CK_DeliveryBoyAdjustments_Type] CHECK ([AdjustmentType] IN ('Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other')),
        [AdjustmentAmount] DECIMAL(18,2) NOT NULL, -- Positive for credits/bonuses, Negative for deductions
        [Reason] NVARCHAR(250) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DeliveryBoyAdjustments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DeliveryBoyAdjustments_CreatedBy] REFERENCES [dbo].[Users]([UserId])
    );

    CREATE NONCLUSTERED INDEX [IX_DeliveryBoyCompensationAdjustments_CompensationId]
    ON [dbo].[DeliveryBoyCompensationAdjustments]([CompensationId])
    INCLUDE ([AdjustmentAmount], [AdjustmentType], [CreatedAt]);
END
GO

-- Explicit Least-Privilege Permissions for DeliveryBoyCompensationAdjustments
DENY INSERT, UPDATE, DELETE ON [dbo].[DeliveryBoyCompensationAdjustments] TO [db_dms_app];
DENY INSERT, UPDATE, DELETE ON [dbo].[DeliveryBoyCompensationAdjustments] TO [dms_app_user];
GRANT SELECT ON [dbo].[DeliveryBoyCompensationAdjustments] TO [db_dms_app];
GO
```

#### Security & Static SQL Invariants:
1. **Direct Modification Blocked:** Direct `INSERT`, `UPDATE`, and `DELETE` permissions are strictly denied to both `db_dms_app` and `dms_app_user`.
2. **Authorized Channel:** All adjustment writes must occur exclusively through stored procedure `dbo.usp_Compensation_ApplyAdjustment`.
3. **Static SQL & Ownership Chaining:** The stored procedure must execute inserts using pure **static SQL**. Dynamic SQL (`sp_executesql`, `EXEC(...)`) is strictly prohibited for ledger writes, guaranteeing that SQL Server's native schema ownership chaining securely executes the insert without requiring direct table permissions.
4. **No UPDATE or DELETE:** Existing adjustment ledger records cannot be updated or deleted. Corrections must be represented by new offsetting adjustment entries.

### 2.3 Stored Procedures Inventory & Signatures

#### 1. `dbo.usp_DeliveryBoy_Register`
- **Purpose:** Atomically registers a field delivery boy, creates staff user account in `dbo.Users`, creates profile in `dbo.DeliveryBoys`, and establishes baseline rate tier in `dbo.DeliveryBoyCommissionRates` if commission-based.
- **Parameters:**
  ```sql
  @Username NVARCHAR(50),
  @PasswordHash NVARCHAR(256),
  @FullName NVARCHAR(100),
  @Phone VARCHAR(15),
  @PrimaryRouteId INT,
  @CompensationType NVARCHAR(20),     -- 'Fixed Salary' or 'Commission'
  @MonthlyBaseSalary DECIMAL(18,2),    -- >= 0.00 (0.00 for commission)
  @InitialCommissionRate DECIMAL(18,2),-- >= 0.00 (0.00 for fixed salary)
  @EffectiveFrom DATE,                -- Mandatory start date for initial rate
  @VehicleNumber NVARCHAR(30) = NULL,
  @Notes NVARCHAR(250) = NULL,
  @AdminUserId INT,
  @DeliveryBoyId INT OUTPUT
  ```
- **Conditional Logic:**
  - If `@CompensationType = 'Fixed Salary'`:
    - `@MonthlyBaseSalary` must be $\ge 0.00$.
    - `@InitialCommissionRate` is forced to `0.00`.
    - No rate tier inserted into `dbo.DeliveryBoyCommissionRates`.
  - If `@CompensationType = 'Commission'`:
    - `@InitialCommissionRate` must be $> 0.00$.
    - `@MonthlyBaseSalary` is forced to `0.00`.
    - Baseline tier inserted into `dbo.DeliveryBoyCommissionRates` (`EffectiveFrom = @EffectiveFrom`, `EffectiveTo = NULL`, `CommissionRatePerLitre = @InitialCommissionRate`).
    - `DeliveryBoys.CurrentCommissionRate` snapshotted to `@InitialCommissionRate`.
- **Error Codes:**
  - `50041`: Phone number already registered to another staff account.
  - `50042`: Invalid compensation parameters (negative salary or rate).
  - `50043`: Primary route does not exist or is inactive.

#### 2. `dbo.usp_DeliveryBoy_GetDirectory`
- **Purpose:** Retrieves complete searchable list of delivery boys with active customer counts and compensation metrics.
- **Parameters:**
  ```sql
  @SearchTerm NVARCHAR(100) = NULL,
  @StatusFilter NVARCHAR(20) = NULL,       -- 'All', 'Active', 'Inactive'
  @CompensationType NVARCHAR(20) = NULL,   -- 'All', 'Fixed Salary', 'Commission'
  @RouteId INT = NULL
  ```
- **Returns:**
  ```text
  DeliveryBoyId, UserId, FullName, Username, Phone, VehicleNumber,
  PrimaryRouteId, RouteName, RouteCode, CompensationType,
  CurrentCommissionRate, MonthlyBaseSalary, ActiveCustomerCount,
  IsActive, CreatedAt
  ```

#### 3. `dbo.usp_DeliveryBoy_GetDetails`
- **Purpose:** Retrieves comprehensive profile details, assigned route, customer roster, rate history, and recent compensations.
- **Parameters:** `@DeliveryBoyId INT`
- **Result Sets:**
  - Set 1: Delivery Boy Profile & User Data.
  - Set 2: Active Assigned Customers Roster (`CustomerId`, `CustomerCode`, `FullName`, `Phone`, `Address`, `RouteName`).
  - Set 3: Commission Rate History (`RateId`, `CommissionRatePerLitre`, `EffectiveFrom`, `EffectiveTo`, `Notes`, `CreatedByName`, `CreatedAt`).
  - Set 4: Recent Monthly Compensations (`CompensationId`, `Month`, `Year`, `CompensationType`, `EligibleCompletedLitres`, `CalculatedCommission`, `FixedSalaryAmount`, `Adjustments`, `NetPayableAmount`, `Status`, `PaidAt`).

#### 4. `dbo.usp_DeliveryBoy_UpdateProfile`
- **Purpose:** Updates editable profile attributes (`FullName`, `Phone`, `VehicleNumber`, `PrimaryRouteId`, `MonthlyBaseSalary`).
- **Parameters:**
  ```sql
  @DeliveryBoyId INT,
  @FullName NVARCHAR(100),
  @Phone VARCHAR(15),
  @VehicleNumber NVARCHAR(30) = NULL,
  @PrimaryRouteId INT,
  @MonthlyBaseSalary DECIMAL(18,2) = NULL,
  @AdminUserId INT
  ```
- **Error Codes:**
  - `50044`: Phone number already in use by another user.
  - `50045`: Delivery boy record does not exist.

#### 5. `dbo.usp_DeliveryBoy_Deactivate`
- **Purpose:** Exact 11-step atomic deactivation transaction.
- **Parameters:**
  ```sql
  @DeliveryBoyId INT,
  @ReassignToDeliveryBoyId INT = NULL, -- Optional replacement agent
  @Reason NVARCHAR(250),
  @AdminUserId INT
  ```
- **Implementation Sequence:**
  1. Lock `DeliveryBoys` row `WITH (UPDLOCK, HOLDLOCK)`. Throw `50045` if not found.
  2. Lock `Users` row `WITH (UPDLOCK, HOLDLOCK)`.
  3. Verify `IsActive == 1`. Throw `50053` if already inactive.
  4. `UPDATE dbo.Users SET IsActive = 0, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @AdminUserId WHERE UserId = @UserId`.
  5. `UPDATE dbo.DeliveryBoys SET IsActive = 0, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @AdminUserId WHERE DeliveryBoyId = @DeliveryBoyId`.
  6. Close open-ended rate tier: `UPDATE dbo.DeliveryBoyCommissionRates SET EffectiveTo = CAST(GETDATE() AS DATE) WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveTo IS NULL`.
  7. Handle customer assignments:
     - If `@ReassignToDeliveryBoyId` provided: verify active, and reassign each customer using `usp_Customer_ReassignDeliveryBoy`.
     - If no replacement: close active assignments (`AssignedTo = CAST(GETDATE() AS DATE), IsActive = 0`) and update `Customers.CurrentDeliveryBoyId = NULL`.
  8. Historical daily deliveries and commission snapshots remain untouched.
  9. Existing monthly compensation records remain intact and payable.
  10. Insert audit record into `dbo.AuditLogs`.
  11. Commit transaction.

#### 6. `dbo.usp_DeliveryBoy_Reactivate`
- **Purpose:** Reactivates a deactivated delivery boy.
- **Parameters:**
  ```sql
  @DeliveryBoyId INT,
  @NewCommissionRate DECIMAL(18,2) = NULL, -- Required if Commission agent
  @AdminUserId INT
  ```
- **Rules:**
  - Atomically sets `DeliveryBoys.IsActive = 1` and `Users.IsActive = 1`.
  - **Commission Employees:** Strictly require an approved commission-rate tier effective on the reactivation date (`@NewCommissionRate > 0.00`). If missing, throw Error `50055: Reactivation of a commission-based delivery boy requires an approved commission rate tier effective on the reactivation date.`
  - **Fixed Salary Employees:** Do NOT require a commission-rate tier (`CurrentCommissionRate = 0.00`, baseline rate tier bypassed). Base salary remains active.
  - **Customer Assignments:** Reactivation must NOT automatically restore historical customer assignments (customers must be explicitly assigned via standard customer management).

#### 7. `dbo.usp_Compensation_CalculateMonthlyDraft`
- **Purpose:** Computes or recalculates monthly draft compensation for a delivery boy for a given calendar month and year.
- **Parameters:**
  ```sql
  @DeliveryBoyId INT,
  @Month TINYINT,
  @Year SMALLINT,
  @AdminUserId INT,
  @CompensationId BIGINT OUTPUT
  ```
- **Rules:**
  - Check existing record: if `Status IN ('Finalized', 'Paid')`, throw Error `50046: Cannot recalculate finalized or paid compensation.`
  - For `Commission`:
    - Query `DailyMilkDeliveries` where `DeliveryBoyId = @DeliveryBoyId`, `EntryDate BETWEEN @MonthStart AND @MonthEnd`, and `Status = 'Completed'`.
    - `EligibleCompletedLitres = ISNULL(SUM(EligibleLitreQuantity), 0.000)`.
    - `CalculatedCommission = ISNULL(ROUND(SUM(EligibleLitreQuantity * CommissionRateSnapshot), 2), 0.00)`.
    - `ApplicableCommissionRate = CASE WHEN EligibleCompletedLitres > 0 THEN ROUND(CalculatedCommission / EligibleCompletedLitres, 2) ELSE 0.00 END`.
    - `FixedSalaryAmount = 0.00`.
    - Sum adjustments from `dbo.DeliveryBoyCompensationAdjustments`:
      `NetPayableAmount = CalculatedCommission + TotalAdjustments`.
  - For `Fixed Salary`:
    - `EligibleCompletedLitres = ISNULL(SUM(EligibleLitreQuantity), 0.000)` (informational only).
    - `CalculatedCommission = 0.00`, `ApplicableCommissionRate = 0.00`.
    - `FixedSalaryAmount = DeliveryBoys.MonthlyBaseSalary`.
    - `NetPayableAmount = FixedSalaryAmount + TotalAdjustments`.
  - Floor check: `NetPayableAmount >= 0.00`.
  - Upsert into `dbo.DeliveryBoyMonthlyCompensations` (`Status = 'Draft'`).

#### 8. `dbo.usp_Compensation_BulkCalculateDrafts`
- **Purpose:** Iterates across all active delivery boys for calendar month/year and invokes draft calculation for each.
- **Parameters:**
  ```sql
  @Month TINYINT,
  @Year SMALLINT,
  @AdminUserId INT,
  @TotalCalculated INT OUTPUT
  ```

#### 9. `dbo.usp_Compensation_ApplyAdjustment`
- **Purpose:** Inserts an adjustment into `dbo.DeliveryBoyCompensationAdjustments` and reconciles parent record within a concurrency-safe transaction.
- **Parameters:**
  ```sql
  @CompensationId BIGINT,
  @AdjustmentType NVARCHAR(30), -- 'Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other'
  @AdjustmentAmount DECIMAL(18,2), -- Signed amount
  @Reason NVARCHAR(250),
  @AdminUserId INT
  ```
- **Concurrency-Safe Transactional Execution:**
  1. `BEGIN TRANSACTION;`
  2. Lock parent `DeliveryBoyMonthlyCompensations` row with `UPDLOCK, HOLDLOCK`. If row does not exist, throw Error `50054: Monthly compensation record does not exist.`
  3. Verify `Status == 'Draft'`. If `Status IN ('Finalized', 'Paid')`, throw Error `50047: Adjustments are strictly restricted to Draft compensation records. Finalized and Paid statements are immutable.`
  4. Recompute existing adjustments from ledger under lock: `SELECT ISNULL(SUM(AdjustmentAmount), 0.00) FROM dbo.DeliveryBoyCompensationAdjustments WITH (UPDLOCK, HOLDLOCK) WHERE CompensationId = @CompensationId`.
  5. Validate non-negative net payable: `GrossEarnings + ExistingAdjustments + @AdjustmentAmount >= 0.00`. If negative, throw Error `50048: Net payable amount cannot be negative. Total deductions exceed gross compensation earnings.`
  6. Insert immutable adjustment row into `dbo.DeliveryBoyCompensationAdjustments` using pure static SQL.
  7. Update parent `DeliveryBoyMonthlyCompensations.Adjustments` and `NetPayableAmount` using the ledger-derived total.
  8. `COMMIT TRANSACTION;`
  - *Invariant:* Concurrent requests cannot bypass the Draft status or non-negative payable check due to `UPDLOCK, HOLDLOCK` on the parent row.
- **Error Codes:**
  - `50047`: Record is already finalized or paid. Adjustments strictly restricted to Draft status.
  - `50048`: Net payable amount cannot be negative (`GrossEarnings + NewTotalAdjustments < 0`).
  - `50049`: Reason notes required (minimum 5 characters).
  - `50054`: Monthly compensation record does not exist.

#### 10. `dbo.usp_Compensation_GetAdjustments`
- **Purpose:** Retrieves all adjustment lines for a monthly compensation record ordered by `CreatedAt ASC`.
- **Parameters:** `@CompensationId BIGINT`

#### 11. `dbo.usp_Compensation_Finalize`
- **Purpose:** Locks monthly compensation statement. Transitions status from `'Draft'` to `'Finalized'`.
- **Parameters:**
  ```sql
  @CompensationId BIGINT,
  @AdminUserId INT
  ```
- **Error Codes:**
  - `50050`: Only Draft compensation records can be finalized.

#### 12. `dbo.usp_Compensation_RecordPayout`
- **Purpose:** Records verified real-world fund disbursement. Transitions status from `'Finalized'` to `'Paid'`.
- **Parameters:**
  ```sql
  @CompensationId BIGINT,
  @PaidAt DATETIME2(7),
  @PaymentReference NVARCHAR(50),
  @Notes NVARCHAR(250) = NULL,
  @AdminUserId INT
  ```
- **Error Codes:**
  - `50051`: Payout can only be recorded on Finalized compensation records.
  - `50052`: Record is already marked Paid.

#### 13. `dbo.usp_Compensation_GetList`
- **Purpose:** Retrieves monthly payroll records with filtering by Month, Year, Status, and CompensationType.
- **Parameters:**
  ```sql
  @Month TINYINT,
  @Year SMALLINT,
  @Status NVARCHAR(20) = NULL,
  @CompensationType NVARCHAR(20) = NULL
  ```

#### 14. `dbo.usp_Compensation_GetDetails`
- **Purpose:** Retrieves full statement details, customer delivery breakdown, and revision audit logs.
- **Parameters:** `@CompensationId BIGINT`

---

## 3. Backend Specification for Kevin (Phase 2)

### 3.1 Domain Models & DTOs
Create in folder `Models/DeliveryBoy/`:
- `DeliveryBoyListItemDto.cs`
- `DeliveryBoyDetailsDto.cs`
- `CreateDeliveryBoyViewModel.cs`
- `EditDeliveryBoyViewModel.cs`
- `DeliveryBoyFilterCriteria.cs`
- `DeactivateDeliveryBoyViewModel.cs`
- `DeliveryBoyMonthlyCompensation.cs`
- `MonthlyCompensationListItemDto.cs`
- `MonthlyCompensationDetailsDto.cs`
- `CompensationAdjustmentDto.cs`
- `ApplyCompensationAdjustmentViewModel.cs`
- `RecordCompensationPayoutViewModel.cs`
- `FinalizeCompensationViewModel.cs`
- `DeliveryBoyDailyDeliveryBreakdownDto.cs`

### 3.2 Repositories
Create in `Repositories/`:
- `IDeliveryBoyRepository.cs` and `DeliveryBoyRepository.cs`
- `ICompensationRepository.cs` and `CompensationRepository.cs`

### 3.3 Services
Create in `Services/`:
- `IDeliveryBoyService.cs` and `DeliveryBoyService.cs`
- `ICompensationService.cs` and `CompensationService.cs`

*Security:* In `DeliveryBoyService.CreateDeliveryBoyAsync`, inject `IPasswordHasher<UserAuthRecord>` to hash initial passwords before invoking `usp_DeliveryBoy_Register`. Never pass raw passwords to SQL or log them.

### 3.4 RouteClassifier Update in `Program.cs`
Update `RouteClassifier.DeliveryBoyBasePaths` in `Program.cs` to remove `"/DeliveryBoy"` and replace with actual field routes:
```csharp
private static readonly string[] DeliveryBoyBasePaths =
{
    "/Home/Privacy",
    "/DailyMilk"
};
```

### 3.5 Dependency Injection Registration
In `Program.cs`, add:
```csharp
// Register Delivery Boy & Compensation Repositories
builder.Services.AddScoped<IDeliveryBoyRepository>(sp => new DeliveryBoyRepository(
    sp.GetRequiredService<ISqlConnectionFactory>(),
    sp.GetRequiredService<ILogger<DeliveryBoyRepository>>()));

builder.Services.AddScoped<ICompensationRepository>(sp => new CompensationRepository(
    sp.GetRequiredService<ISqlConnectionFactory>(),
    sp.GetRequiredService<ILogger<CompensationRepository>>()));

// Register Application Services
builder.Services.AddScoped<IDeliveryBoyService, DeliveryBoyService>();
builder.Services.AddScoped<ICompensationService, CompensationService>();
```

### 3.6 Automated Test Harness (`scratch/Day6BackendVerification`)
Author a console harness validating all 11 backend test cases. Must run cleanly, clean up test artifacts, and verify master table count invariants.

---

## 4. Frontend Specification for Viraj (Phase 3)

### 4.1 Controller Specification: `Controllers/DeliveryBoyController.cs`
Decorate with:
```csharp
[Authorize(Roles = "Admin")]
public class DeliveryBoyController : Controller
```
Inject non-nullable dependencies:
- `IDeliveryBoyService`
- `ICompensationService`
- `IRouteService`
- `ILogger<DeliveryBoyController>`

### 4.2 Error Handling & Security Invariant
- Never expose `sqlEx.Message` or `ex.Message` through `TempData["ErrorMessage"]`.
- Log all technical exceptions server-side via `_logger.LogError(...)`.
- Provide generic, user-friendly messages guiding the user to retry or contact support.
- Preserve business validation messages (e.g., minimum character length, positive amounts).

### 4.3 Views Specification (`Views/DeliveryBoy/`)
- `Index.cshtml`: Admin emerald theme, KPI stat cards, search & filter toolbar, responsive table, pagination.
- `Create.cshtml`: Bootstrap 5 form, dynamic JavaScript toggles between Fixed Salary and Commission fields, client-side validation.
- `Details.cshtml`: Tabbed interface (Assigned Customers, Commission Rate Tiers, Monthly Compensation History), action buttons.
- `Edit.cshtml`: Clean profile editing with route selector.
- `Compensation.cshtml`: Payroll workspace, month/year selector, summary cards, draft generation action.
- `CompensationDetails.cshtml`: Professional printable monthly compensation statement, daily delivery log table, adjustment history, action modals.

### 4.4 Admin Layout Sidebar Integration
In `Views/Shared/_LayoutAdmin.cshtml`:
- Item 2:
  ```html
  <li class="nav-item">
      <a class="nav-link @(ViewData["ActiveMenu"]?.ToString() == "DeliveryBoy" ? "active" : "")" href="~/DeliveryBoy">
          <i class="nav-icon cil-bike"></i> Delivery Boy Management
      </a>
  </li>
  ```
- Strict 10-item order, emerald theme, zero forbidden items.

### 4.5 Automated Test Harness (`scratch/Day6FrontendVerification`)
Author a console harness validating all 8 frontend test cases.

---

## 5. Acceptance Criteria & Guardrails

1. **Clean Build:** `dotnet build --no-incremental` must pass with 0 Warnings and 0 Errors.
2. **Pristine Financial Invariants:** Zero financial corruption. `dbo.DeliveryBoyMonthlyCompensations` and `dbo.DeliveryBoyCompensationAdjustments` must reflect deterministic mathematical formulas. Master tables (`Customers`, `Routes`, `MilkCategories`) must remain intact.
3. **No Direct DML:** All database writes must execute via dedicated stored procedures under `db_dms_app`.
4. **Git Discipline:** Strictly no git write operations without explicit Lead Architect authorization.
