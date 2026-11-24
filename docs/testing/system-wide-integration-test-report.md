# Dairy Management System — System-Wide Integration Test & Stabilization Report

**Document Version:** 1.1.0  
**Audit & Stabilization Date:** November 24, 2026  
**Auditor:** Kevin Gajera (Senior SQL Server Database Engineer & ADO.NET Backend Developer)  
**Target Repository:** `https://github.com/RachitParsana/Dairy_Management_System.git`  
**Target Branch:** `main`  
**Audited Baseline HEAD:** `f2f04fc36fae021b62932323076df57142657314`  
**Execution Environment:** Windows 10 x64, .NET 9.0.315, SQL Server 2019 LocalDB (`MSSQLLocalDB`), Database `DairyManagementDb`  
**Security Context:** Strictly least-privilege `dms_app_user` (`db_dms_app` database role)  

---

## 1. Executive Summary

Following explicit authorization for targeted database permission stabilization (DEF-01 and DEF-02), a targeted stabilization migration `sql/12_StabilizationPermissionFixes.sql` was authored, deployed, and rigorously verified across `DairyManagementDb`. 

The stabilization adhered strictly to the principle of least privilege:
- **Zero Table-Wide SELECT Grants:** No table-wide SELECT was granted on `dbo.Users` or `dbo.CustomerDeliveryBoyAssignments`.
- **Narrow Column-Level Access:** SELECT grants were restricted strictly to the explicit columns read and filtered by `UserAuthRepository` and `CustomerRepository`.
- **Preserved Boundaries:** Role memberships (`db_owner`, `db_datareader`, `db_datawriter`) were untouched (0 for `dms_app_user`).
- **Append-Only Financial Ledger Protection:** `PaymentAllocationEvents` direct DML remains denied (`SqlException 229`).
- **Stored Procedure Encapsulation:** `NotificationPopupSeen` direct DML remains denied; ownership chaining continues to function cleanly.

### Post-Stabilization Regression Test Metrics

| Test Category | Total Tests | Passed | Failed | Blocked | Pass Rate |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Authentication Regression Suite (DEF-01)** | 6 | 6 | 0 | 0 | 100.0% |
| **Customer Details Regression Suite (DEF-02)** | 3 | 3 | 0 | 0 | 100.0% |
| **Security & Least-Privilege Suite** | 4 | 4 | 0 | 0 | 100.0% |
| **Baseline & Technology Compliance** | 3 | 3 | 0 | 0 | 100.0% |
| **Core Business Workflows (Dairy, Billing, DB, Dashboards)** | 17 | 17 | 0 | 0 | 100.0% |
| **UI & Browser Architecture Audit** | 5 | 5 | 0 | 0 | 100.0% |
| **Total System Integration & Regression Suite** | **38** | **38** | **0** | **0** | **100.0%** |

---

## 2. Permission Comparison: Before vs. After Stabilization

### DEF-01: `dbo.Users` Column-Level Permissions

| Column Name | Permission Before (`db_dms_app`) | Permission After (`sql/12_StabilizationPermissionFixes.sql`) | Access Status |
| :--- | :---: | :---: | :---: |
| `UserId` | GRANT SELECT | GRANT SELECT | Read by Auth Repository |
| `Username` | *None (Denied - Error 230)* | **GRANT SELECT** | Read & Filtered by Auth Repository |
| `PasswordHash` | *None (Denied - Error 230)* | **GRANT SELECT** | Read for PBKDF2 Verification |
| `Role` | *None (Denied - Error 230)* | **GRANT SELECT** | Read & Filtered by Auth Repository |
| `FullName` | GRANT SELECT | GRANT SELECT | Read by Auth Repository |
| `Phone` | *None (Denied - Error 230)* | **GRANT SELECT** | Read & Filtered by Auth Repository |
| `IsActive` | *None (Denied - Error 230)* | **GRANT SELECT** | Read & Filtered by Auth Repository |
| `LastLoginAt` | *None (Denied - Error 230)* | **GRANT SELECT** | Read by Auth Repository |
| `CreatedAt` | *None* | *None* | **DENIED (Error 230)** |
| `CreatedBy` | *None* | *None* | **DENIED (Error 230)** |
| `UpdatedAt` | *None* | *None* | **DENIED (Error 230)** |
| `UpdatedBy` | *None* | *None* | **DENIED (Error 230)** |
| **Table-Level SELECT** | **None** | **None** | **Strictly Withheld** |
| **Table-Level UPDATE** | GRANT UPDATE | GRANT UPDATE | Required for `UpdateLastLoginAtAsync` |

---

### DEF-02: `dbo.CustomerDeliveryBoyAssignments` Column-Level Permissions

| Column Name | Permission Before (`db_dms_app`) | Permission After (`sql/12_StabilizationPermissionFixes.sql`) | Access Status |
| :--- | :---: | :---: | :---: |
| `AssignmentId` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `CustomerId` | *None (Denied - Error 229)* | **GRANT SELECT** | Filtered by `GetAssignmentHistoryAsync` |
| `DeliveryBoyId` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `AssignedFrom` | *None (Denied - Error 229)* | **GRANT SELECT** | Read & Ordered by History Query |
| `AssignedTo` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `IsActive` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `Notes` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `CreatedAt` | *None (Denied - Error 229)* | **GRANT SELECT** | Read by `GetAssignmentHistoryAsync` |
| `CreatedBy` | *None* | *None* | **DENIED (Error 230)** |
| `UpdatedAt` | *None* | *None* | **DENIED (Error 230)** |
| `UpdatedBy` | *None* | *None* | **DENIED (Error 230)** |
| **Table-Level SELECT** | **None** | **None** | **Strictly Withheld** |
| **Table-Level DML** | GRANT INSERT, UPDATE, DELETE | GRANT INSERT, UPDATE, DELETE | Preserved from migration 06 |

---

## 3. Targeted Regression Test Results

All regression tests were executed under the restricted application database user context `dms_app_user` (`db_dms_app`).

### A. Authentication Regression Tests (DEF-01)

| Test ID | Test Scenario | Expected Outcome | Actual Evidence under `dms_app_user` | Status |
| :--- | :--- | :--- | :--- | :---: |
| **REG-AUTH-01** | Delivery Boy login with valid credentials | `AuthResult.Succeeded == true`, `User.Role == 'DeliveryBoy'`, `User.FullName` populated. | `Succeeded=True, User='Ramesh Kumar', Role='DeliveryBoy'` | **PASS** |
| **REG-AUTH-02** | Admin login with valid credentials | `AuthResult.Succeeded == true`, `User.Role == 'Admin'`, `LastLoginAt` timestamp updated in SQL. | `Succeeded=True, User='QA Test Admin', Role='Admin'` | **PASS** |
| **REG-AUTH-03** | Invalid credentials fail safely | `AuthResult.Succeeded == false`, generic error message without system disclosures. | `Succeeded=False, Error='Invalid mobile number or password.'` | **PASS** |
| **REG-AUTH-04** | Inactive user login rejection | `AuthResult.Succeeded == false`, deactivated user (`IsActive = 0`) rejected. | `Rejected successfully (Succeeded=False, Error='Invalid mobile number or password.')` | **PASS** |
| **REG-AUTH-05** | Credential leakage prevention in DTOs | `AuthenticatedUser` class contains zero password or password hash properties. | Verified via reflection: `[UserId, Username, FullName, Phone, Role, IsActive]`. Zero password/hash fields. | **PASS** |
| **REG-AUTH-06** | Unneeded Users columns remain inaccessible | Querying `CreatedAt, CreatedBy, UpdatedAt, UpdatedBy` throws `SqlException 230`. | Denied as expected: `SqlException (Number 230): The SELECT permission was denied on the column 'CreatedAt' of the object 'Users'`. | **PASS** |

### B. Customer Details Regression Tests (DEF-02)

| Test ID | Test Scenario | Expected Outcome | Actual Evidence under `dms_app_user` | Status |
| :--- | :--- | :--- | :--- | :---: |
| **REG-CUST-01** | Customer details profile and assignment history retrieval | `CustomerDetailsViewModel` loaded with active subscriptions and assignment history without `SqlException 229`. | Retrieved Customer #1 (Rajesh Patel), Assignment History loaded cleanly. | **PASS** |
| **REG-CUST-02** | Customer controller access authorization | `CustomerController` class decorated with `[Authorize(Roles = "Admin")]`. | Verified via reflection: class attribute `[Authorize(Roles = 'Admin')]` present. | **PASS** |
| **REG-CUST-03** | Unneeded assignment columns remain inaccessible | Querying `CreatedBy, UpdatedAt, UpdatedBy` throws `SqlException 230`. | Denied as expected: `SqlException (Number 230): The SELECT permission was denied on the column 'CreatedBy' of the object 'CustomerDeliveryBoyAssignments'`. | **PASS** |

### C. Security & Least-Privilege Boundary Tests

| Test ID | Security Scenario | Expected Boundary | Actual Evidence under `dms_app_user` | Status |
| :--- | :--- | :--- | :--- | :---: |
| **REG-SEC-01** | Application role membership verification | `dms_app_user` is member of `db_dms_app` only; not `db_owner`, `db_datareader`, or `db_datawriter`. | Verified: `IsDmsApp=1, IsOwner=0, IsDataReader=0, IsDataWriter=0`. | **PASS** |
| **REG-SEC-02** | Append-only financial ledger protection | Direct `INSERT`, `UPDATE`, `DELETE` on `dbo.PaymentAllocationEvents` is denied. | Verified: Direct `INSERT` threw `SqlException 229 (The INSERT permission was denied on the object 'PaymentAllocationEvents')`. | **PASS** |
| **REG-SEC-03** | Notification popup seen table encapsulation | Direct `INSERT`, `UPDATE`, `DELETE` on `dbo.NotificationPopupSeen` is denied. | Verified: Direct `INSERT` threw `SqlException 229 (The INSERT permission was denied on object 'NotificationPopupSeen')`. | **PASS** |
| **REG-SEC-04** | Stored procedure ownership chaining verification | Stored procedure execution succeeds even though direct table SELECT is denied. | Direct `SELECT * FROM dbo.AppSettings` failed with `SqlException 229`, while `EXEC dbo.usp_AppSettings_GetAll` succeeded via ownership chaining. | **PASS** |

---

## 4. Full Integration Workflow Verification (Passing)

| Test ID | Module | Scenario | Expected Outcome | Actual Evidence | Status |
| :--- | :--- | :--- | :--- | :--- | :---: |
| **TC-BASE-01** | Architecture | Technology stack compliance | .NET 9 Web App, pure ADO.NET, zero EF Core, zero SPA. | `<TargetFramework>net9.0</TargetFramework>`, `Microsoft.Data.SqlClient` v7.1.2. | **PASS** |
| **TC-BASE-02** | Architecture | Core Application Services DI registration | All 10 services and repositories registered. | Verified in `Program.cs`. | **PASS** |
| **TC-BASE-03** | Database | Canonical SQL migration scripts | Exactly 12 migration scripts present in `sql/`. | `01_CreateDatabase.sql` through `12_StabilizationPermissionFixes.sql` verified. | **PASS** |
| **TC-AUTH-03** | Routing | RouteClassifier boundary enforcement | Strictly enforces delivery boy portal paths; no prefix bleed. | `/Home/Privacy` matched, `/DailyMilk/Index` matched, prefix bleed rejected. | **PASS** |
| **TC-MILK-01** | Daily Milk | Assigned runsheet roster retrieval | Customer roster ordered by `DeliverySequence`. | Retrieved customer roster for Delivery Boy (Ramesh Kumar, UserId 2004). | **PASS** |
| **TC-MILK-02** | Daily Milk | Commission eligibility verification | Commission eligible only for `Status == 'Delivered'` and `DeliveryBoyId > 0`. | Verified in `DailyMilkService.cs`. | **PASS** |
| **TC-BILL-01** | Billing & Payments | Manual payment allocation (NO FIFO) | Zero automatic FIFO payment allocation routines. | Verified in `BillingService.cs`. Explicit invoice selection enforced. | **PASS** |
| **TC-BILL-02** | Billing & Payments | Append-only ledger 4 canonical event types | DB check constraint restricts event types strictly to canonical 4. | Verified in `sys.check_constraints`: `InitialAllocation, ReversalAdjustment, VoidReversal, AdvanceCreditAllocation`. | **PASS** |
| **TC-COMP-01** | Compensation | Delivery partner directory query | Directory list returns partner profiles. | Retrieved master records with route assignments. | **PASS** |
| **TC-COMP-02** | Compensation | Partner details and commission history | Returns partner profile with route and tier details. | Retrieved partner profile with route code. | **PASS** |
| **TC-COMP-03** | Compensation | Monthly compensation lifecycle state machine | Enforces `Draft` -> `Finalized` -> `Paid` with concurrency locks. | Verified `CK_DeliveryBoyMonthlyCompensations_Status` and stored procedures. | **PASS** |
| **TC-DASH-01** | Dashboards | Admin dashboard KPI multi-result-set query | Returns aggregated KPIs, deliveries, and aging breakdown. | Retrieved KPIs and metrics without error. | **PASS** |
| **TC-DASH-02** | Dashboards | Delivery Boy dashboard query | Returns portal metrics resolved by authenticated `UserId`. | Retrieved partner metrics including assigned route drops. | **PASS** |
| **TC-REP-01** | Reports | Monthly billing vs collection trends | Returns 6-month historical dataset for analytics charts. | Returned monthly trend data points. | **PASS** |
| **TC-REP-02** | Reports | Month-based customer balance aging | Balances strictly bucketed by calendar months. | Aging report returned with month columns (1mo, 2mo, 3mo, 4-5mo, 6+mo). | **PASS** |
| **TC-EXP-01** | Exports | Customer master CSV streaming | Emits RFC-4180 CSV byte array with UTF-8 BOM encoding. | Byte array generated with leading bytes `0xEF, 0xBB, 0xBF`. | **PASS** |
| **TC-EXP-02** | Exports | All 5 standard CSV exports | Deliveries, Bills, Payments, and Compensation emit UTF-8 BOM. | All 5 CSV generators return valid RFC-4180 UTF-8 BOM streams. | **PASS** |
| **TC-NOTIF-01** | Notifications | Delivery boy alert feed retrieval | Returns active notifications targeted to role or user. | Retrieved alert feed for Delivery Boy. | **PASS** |
| **TC-SETT-01** | Settings | System settings retrieval | Settings retrieved cleanly; `BillingCycleDueDays` prohibited. | Retrieved settings; confirmed `BillingCycleDueDays` is absent from DTOs. | **PASS** |
| **TC-SEC-05** | Security | Parameterized SQL across all repositories | All 14 repository classes use `SqlParameter` or Stored Procedures. | Confirmed zero string-interpolated or concatenated dynamic SQL. | **PASS** |
| **TC-UI-01** | UI | Admin Sidebar layout and styling | 10 canonical navigation items in order, zero dead `#` hrefs, emerald style. | Verified in `_LayoutAdmin.cshtml`. | **PASS** |
| **TC-UI-02** | UI | Delivery Boy Sidebar layout and styling | 8 canonical navigation items in order, zero dead `#` hrefs, blue field style. | Verified in `_LayoutDeliveryBoy.cshtml`. | **PASS** |
| **TC-UI-03** | UI | Chart.js graceful degradation | Graceful degradation alert when Chart.js CDN is unavailable. | Verified fallback alert rendering in all chart views. | **PASS** |
| **TC-UI-04** | UI | Centralized Error View | Handles 404, 403, 500 status codes with role-aware return navigation. | Card treatments and role-aware navigation verified in `Error.cshtml`. | **PASS** |
| **TC-UI-05** | UI | Delivery Boy Settings state display | Honest representation of local device/session preference persistence. | Preference controls clearly scoped without false claim of DB save. | **PASS** |

---

## 5. Build Result and Changed-File Manifest

### Build Status
```
dotnet build Dairy_Management_System.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.75
```

### Git Diff & Working Tree State
```
git status
On branch main
Your branch is up to date with 'origin/main'.

Untracked files:
    docs/testing/
    scratch/
    sql/12_StabilizationPermissionFixes.sql

nothing added to commit but untracked files present
```
* **Modified Tracked Files:** None (`git diff` is completely empty).
* **New Migration Script:** `sql/12_StabilizationPermissionFixes.sql` (untracked, ready for commit authorization).
* **Test Artifacts:** `docs/testing/system-wide-integration-test-report.md`, `scratch/SystemWideIntegrationTester/`.

---

## 6. Remaining Risks & Roadmap Considerations

1. **Delivery Boy Preferences Persistence:** Currently, sound alerts and sunlight contrast toggles operate as client device/session preferences. A future schema enhancement could persist delivery partner preferences to a dedicated database table if multi-device persistence is required.
2. **Periodic Notification Reconciliation:** The background reconciliation service (`usp_Notifications_ReconcilePublishedBills`) is registered as a hosted service. Ensure scheduled background task execution is monitored in production environments.
3. **No Financial or Security Regressions:** The append-only ledger `PaymentAllocationEvents` is strictly guarded by `DENY` permissions and a database CHECK constraint ensuring only the 4 canonical event types are recorded. No automatic FIFO exists.
