# Day 7 — Final Architecture & System Integration Handoff

**Document Version:** 1.2.0 (Final Approved Architecture Baseline)  
**Project:** Dairy Management System (`D:\MU\.Net_Code\Dairy_Management_System`)  
**Shared Repository:** `https://github.com/RachitParsana/Dairy_Management_System.git`  
**Verified Git Baseline:** `4f1005b01b680133cf45ffaecb38be7287beef8e` (`main`)  
**Target Framework:** .NET 9.0 (C# 13)  
**Architectural Pattern:** Layered Enterprise Architecture (ASP.NET Core MVC + ADO.NET)  
**Author:** Rachit Parsana, Lead Solution Architect & Technical Lead  
**Recipients:** Kevin Gajera (Senior SQL & ADO.NET Engineer), Viraj Bodar (Senior ASP.NET Core MVC Frontend Engineer)  

---

## Table of Contents
1. [Executive Summary & Scope of Day 7 Integration](#1-executive-summary--scope-of-day-7-integration)
2. [Mandatory Architectural Principles & Business Invariants](#2-mandatory-architectural-principles--business-invariants)
   - 2.1 [Payment Allocation — Single Selected Invoice & Advance Credit Invariant](#21-payment-allocation--single-selected-invoice--advance-credit-invariant)
   - 2.2 [Billing Due Date & Status Invariant](#22-billing-due-date--status-invariant)
   - 2.3 [Customer Balance Ageing Contract (Month-Based)](#23-customer-balance-ageing-contract-month-based)
   - 2.4 [Notification Lifecycle, Idempotency & One-Time Popup](#24-notification-lifecycle-idempotency--one-time-popup)
   - 2.5 [Settings & Audit Invariant](#25-settings--audit-invariant)
   - 2.6 [Delivery Boy Role & Operational Data Isolation](#26-delivery-boy-role--operational-data-isolation)
3. [Existing Implementation Audit Matrix](#3-existing-implementation-audit-matrix)
4. [Notification Integration with Billing Publication](#4-notification-integration-with-billing-publication)
5. [Reporting Contracts](#5-reporting-contracts)
6. [Export Contracts & Data Access Mechanisms](#6-export-contracts--data-access-mechanisms)
7. [Delivery Boy Identity Resolution & Operational Roster](#7-delivery-boy-identity-resolution--operational-roster)
8. [System Settings & Configuration](#8-system-settings--configuration)
9. [Kevin's Consolidated Backend Handoff (SQL & ADO.NET)](#9-kevins-consolidated-backend-handoff-sql--adonet)
10. [Viraj's Consolidated Frontend Handoff (MVC & Razor)](#10-virajs-consolidated-frontend-handoff-mvc--razor)
11. [Final Testing & Submission-Readiness Checklist](#11-final-testing--submission-readiness-checklist)
12. [Confirmation of Baseline & Day 1–6 Architectural Integrity](#12-confirmation-of-baseline--day-16-architectural-integrity)

---

## 1. Executive Summary & Scope of Day 7 Integration

Today is **Day 7**, our final planned development and system integration milestone. Over Days 1 through 6, our engineering team has successfully engineered, verified, and landed the core domain modules of the Dairy Management System:
- **Days 1 & 2:** Relational schema, Least-Privilege connection pooling, Cookie Authentication, PBKDF2 cryptography, Role-Based Access Control (`Admin`, `DeliveryBoy`).
- **Day 3:** Customer Master Management, Subscription Rate Cards, Delivery Sequence, Vacation Tracking & Lockouts.
- **Day 4:** Daily Milk Field Runsheets, Mobile Distribution Entry, Vacation Exclusions, Unscheduled Authorizations, Audited Corrections.
- **Day 5:** Monthly Calendar Billing Generation, Revision History, Cash/Online Payments, Event-Ledger Allocation, Payment Voiding.
- **Day 6:** Delivery Boy Personnel Master, Shift Commission Rates, Monthly Compensation Calculation (Fixed Salary vs. Commission), Adjustments, and Payout Disbursals.

This master document establishes the unified, approved, and non-iterative architecture and technical handoff for all remaining functionality:
1. **Admin Dashboard:** Real-time SQL aggregations replacing static placeholder metrics.
2. **Centralized Reports & Analytics:** Chart.js visualizations for 6-month financial trends, daily milk consumption volumes, route delivery performance, and approved month-based customer balance aging.
3. **Export Management Hub:** Dedicated multi-entity CSV streaming engine (Customers, Deliveries, Monthly Bills, Payments, Compensation).
4. **Notification Management Subsystem:** Broadcast communication, automated midnight bill publication alerts, delivery boy one-time popup experience, and read/seen acknowledgement semantics.
5. **System Settings & Configuration:** Non-financial enterprise metadata management and delivery partner profile preferences.
6. **Help & Support:** Operational manuals, workflow guides, and emergency escalation paths.
7. **End-to-End Delivery Boy Portal Integration:** Unifying runsheets, customer rosters, route alerts, and live dashboard metrics.
8. **Application-wide Error Handling & UI Polish:** Centralized status code handling (404, 403, 500) and design system consistency.

---

## 2. Mandatory Architectural Principles & Business Invariants

### 2.1 Payment Allocation — Single Selected Invoice & Advance Credit Invariant
- **No Automatic FIFO Allocation:** The system **never** cascades payments chronologically across past invoices.
- **Explicit Single-Invoice Allocation:** When an administrator records a payment (`usp_Payment_RecordAndAllocateSingle`), the allocation targets **exactly one explicitly selected invoice** (`@SelectedBillId`).
- **Unallocated Advance Credit:** If the payment amount exceeds the selected invoice balance (`@Amount > @BillBalance`), the unallocated surplus remains on the customer's account as unallocated advance credit (`AdvanceCreditBalance`).
- **Advance Credit Allocation:** Unallocated advance credit can be applied only by an explicit administrative action targeting one selected invoice via `usp_Payment_AllocateAdvanceCreditSingle`.
- **Append-Only Event Ledger:** `dbo.PaymentAllocationEvents` is the authoritative, immutable event ledger. The allowed event types are strictly:
  1. `InitialAllocation` (Standard allocation of new payment receipt to invoice)
  2. `ReversalAdjustment` (Debit/Credit adjustments from bill revision)
  3. `VoidReversal` (Full reversal of allocation when payment receipt is voided)
  4. `AdvanceCreditAllocation` (Subsequent application of advance credit to an invoice)
  *(There is no `AdvanceCreditCreated` event; unallocated advance credit is derived dynamically from payment receipt total minus sum of allocation events).*
- **Immutability:** Historical payment and allocation records are never modified in-place.

### 2.2 Billing Due Date & Status Invariant
- **Optional Payment Due Date:** `PaymentDueDate` on `dbo.MonthlyBills` can remain `NULL`.
- **No Automatic Default Due Date:** The system does not enforce an automatic default payment due date (`BillingCycleDueDays` is prohibited).
- **Overdue Invariant:** An invoice **cannot** be marked `Overdue` if `PaymentDueDate IS NULL`. When `PaymentDueDate` is `NULL`, unpaid invoices remain `Unpaid` or `Partially Paid` regardless of elapsed calendar time.
- **Immutability of Published Due Dates:** Once a bill is published, its `PaymentDueDate` remains immutable. Settings must never silently alter historical or published invoice due dates.

### 2.3 Customer Balance Ageing Contract (Month-Based)
- **Approved Ageing Buckets:** Customer outstanding balances are categorized strictly into:
  - **`1 month`** (Unpaid balance from the most recent closed billing cycle)
  - **`2 months`** (Unpaid balance 2 cycles prior)
  - **`3 months`** (Unpaid balance 3 cycles prior)
  - **`4–5 months`** (Unpaid balance 4 to 5 cycles prior)
  - **`6+ months`** (Unpaid balance 6 or more cycles prior)
- **Calculation Basis:** Ageing is calculated strictly using **unpaid invoice portions** (`MonthlyBills.BalanceAmount`), never original billed amounts.

### 2.4 Notification Lifecycle, Idempotency & One-Time Popup
- **One-Time Midnight Bill Publication:** When bills are published (individually or in bulk), a single `BillPublished` notification is emitted for the customer's route.
- **Strict Database Idempotency:** The notification procedure checks:
  ```sql
  WHERE BillId = @BillId AND NotificationType = 'BillPublished'
  ```
  Republishing or revising a bill never produces duplicate `BillPublished` notifications.
- **One-Time Popup Experience:** When the delivery boy logs into the mobile portal (`/Home/Privacy`), unacknowledged `BillPublished` notifications trigger a one-time modal popup displaying newly published route bills. Dismissing or viewing the popup clears the popup flag for that session, but the notification remains permanently accessible in the Notification Center (`/Notification/DeliveryBoyNotifications`).
- **Read/Seen Acknowledgement Semantics:** When a delivery boy clicks "Acknowledge", `dbo.NotificationAcknowledgements` records only that the employee has read/seen the notification (`AcknowledgedAt = SYSUTCDATETIME()`). Acknowledgement carries **zero payment, collection, settlement, or balance-altering side effect**.
- **Historical Non-Mutation:** Emitting or acknowledging notifications never alters billing amounts, payment allocations, or customer ledgers.

### 2.5 Settings & Audit Invariant
- **No Fabricated Seed Data:** `dbo.AppSettings` is preserved with its existing schema (`SettingId`, `SettingKey`, `SettingValue`, `DataType`, `Description`, `UpdatedAt`, `UpdatedBy`). No fake phone numbers, email addresses, or dairy names are seeded into the database.
- **Neutral Display:** Unconfigured parameters remain unset in the database and display neutral placeholders (`"Not Configured"` / blank) in the UI.
- **Strict Scope:** Settings manage only non-financial enterprise metadata (e.g. Dairy Name, Contact Details, Address, Currency display, System Announcement). Settings can **never** introduce billing rules, due-date policies, or financial recalculations.
- **Audit Logging:** Every setting change must be audited in `dbo.AuditLogs` with `OldValuesJson` and `NewValuesJson`.

### 2.6 Delivery Boy Role & Operational Data Isolation
- **Operational Data Isolation:** An authenticated delivery boy can access only their own assigned operational data (customers on their assigned route, their own daily milk entries, their own route notifications, and their own compensation breakdown).
- **Zero Financial Permissions:** Delivery boys have read-only access to customer contact details and shift preferences on their assigned route. Delivery boys have **zero payment recording, payment voiding, bill generation, or ledger modification permissions**.

---

## 3. Existing Implementation Audit Matrix

An audit of all repository files, SQL scripts, models, controllers, services, and views against baseline `4f1005b01b680133cf45ffaecb38be7287beef8e` confirms the following implementation status across all 10 areas:

| Area | Status | Existing Code & Artifacts | Identified Gaps | Technical Strategy |
| :--- | :--- | :--- | :--- | :--- |
| **1. Admin Dashboard** | **Partially implemented** | `HomeController.Index`, `Views/Home/Index.cshtml`, partial `IBillingService` injection. | Metric cards display `--` placeholders; deliveries table has 7 static mock HTML rows; subscription form is demo HTML. | Replace with `AdminDashboardViewModel` bound to `usp_Dashboard_GetAdminMetrics` (live SQL multi-result set). |
| **2. Reports & Analytics** | **Partially implemented** | `BillingController.Reports`, `Views/Billing/Reports.cshtml`, `usp_BillingReports_GetMonthlySummary`. | Item 5 in `_LayoutAdmin.cshtml` links to dead anchor `#reports`. Billing report is isolated; no unified reporting hub, no milk volume consumption report, no approved month-based customer aging report. | Create `ReportsController`, `IReportsService`, and `Views/Reports/Index.cshtml` with Chart.js feeds and approved month-based ageing. |
| **3. Export Management** | **Partially implemented** | `CompensationController.ExportCsv` (Day 6). | Item 6 in `_LayoutAdmin.cshtml` links to dead anchor `#export-management`. Cannot export Customers, Deliveries, Bills, or Payments. | Create `ExportController`, `IExportService`, and `Views/Export/Index.cshtml` streaming RFC 4180 CSVs. |
| **4. Notification Management** | **Not implemented (App Layer)** | SQL Tables `dbo.Notifications`, `dbo.NotificationAcknowledgements`, Index `IX_Notifications_Target`. | Zero C# domain models, DTOs, repositories, services, or controllers exist. Menu links are dead anchors. Bill publication emits no alerts. | Create `Notification` C# stack, wire billing publication trigger, mobile alert view, one-time popup, and topbar bell badge. |
| **5. Settings & Configuration** | **Not implemented (App Layer)** | SQL Table `dbo.AppSettings` in script 03. | Zero C# code. Menu links are dead anchors. No administrative interface to view/edit enterprise profile parameters. | Create `Settings` C# stack, bind non-financial parameters, display neutral placeholders if unconfigured, enforce audit logging in `AuditLogs`. |
| **6. Help & Support** | **Not implemented** | None. | Sidebar links in Admin (`#help-support`) and Delivery Boy (`#help-support`) are dead anchors. No documentation views exist. | Create `HelpSupportController` and role-aware operational workflow documentation views. |
| **7. Admin & Delivery Boy Integration** | **Requires integration** | `DailyMilkController`, `_LayoutDeliveryBoy.cshtml`, `Views/Home/Privacy.cshtml`. | Delivery Boy dashboard (`/Home/Privacy`) shows static `--` placeholders. Sidebar links for `My Customers`, `Notifications`, `Settings`, `Help`, and `Profile` are dead `#` anchors. | Wire `My Customers` roster view, live delivery boy dashboard metrics, one-time bill popup, and complete 8-item navigation. |
| **8. Error Handling & Validation** | **Partially implemented** | `HomeController.Error`, standard `Views/Shared/Error.cshtml`, try/catch blocks. | Default ASP.NET Core template; no status code execution handler; 404/403/500 errors leak unstyled pages. | Add `UseStatusCodePagesWithReExecute` in `Program.cs` and style `Views/Shared/Error.cshtml`. |
| **9. Database Integration & Security** | **Complete / Requires verification** | Scripts 01–10, `ISqlConnectionFactory`, `DbInitializer`, parameterized ADO.NET. | All core financial procedures exist. Script 11 must be created for Day 7 dashboard, reporting, export, notification, and settings procedures. | Create `sql/11_FinalSystemIntegration.sql`, granting execute permission strictly to `db_dms_app`. |
| **10. Application Configuration** | **Complete / Requires verification** | `appsettings.json`, `appsettings.Development.json`, `launchSettings.json`. | Clean configurations present. `Program.cs` DI container must register all new repositories and services cleanly. | Register services in `Program.cs` without circular dependencies; verify clean build. |

---

## 4. Notification Integration with Billing Publication

```mermaid
sequenceDiagram
    autonumber
    actor Admin as Administrator
    participant BillingSvc as BillingService
    participant NotifSvc as NotificationService
    participant NotifRepo as NotificationRepository
    participant DB as SQL Server (dbo.Notifications)
    actor DeliveryBoy as Delivery Boy (Mobile)

    Admin->>BillingSvc: PublishBillAsync(billId, graceDays, adminUserId)
    BillingSvc->>DB: EXEC usp_MonthlyBilling_PublishBill
    Note over BillingSvc,DB: Bill published; PaymentDueDate set or NULL
    BillingSvc->>NotifSvc: EmitBillPublishedNotificationAsync(billId, adminUserId)
    NotifSvc->>NotifRepo: CreateBillPublicationNotificationAsync(billId, adminUserId)
    NotifRepo->>DB: EXEC usp_Notifications_EmitBillPublished
    Note over DB: Idempotency Check: IF NOT EXISTS (BillId AND BillPublished)<br/>Insert TargetRole='DeliveryBoy', TargetRouteId, TargetDeliveryBoyId
    DB-->>NotifRepo: NotificationId created (or skipped if exists)
    
    DeliveryBoy->>DeliveryBoy: Opens Portal (/Home/Privacy)
    DeliveryBoy->>NotifSvc: GetUnacknowledgedBillPopupsAsync(deliveryBoyId)
    NotifSvc-->>DeliveryBoy: List of newly published bills for route
    Note over DeliveryBoy: Triggers One-Time Modal Popup (#billPublishedPopupModal)<br/>Dismissing clears popup flag for session
    
    DeliveryBoy->>NotifSvc: GetUserNotificationsAsync(userId, "DeliveryBoy")
    NotifSvc->>DB: EXEC usp_Notifications_GetForUser
    DB-->>DeliveryBoy: Complete persistent notification history
    DeliveryBoy->>NotifSvc: AcknowledgeNotificationAsync(notificationId, userId)
    NotifSvc->>DB: EXEC usp_Notifications_Acknowledge
    Note over DB: Inserts into dbo.NotificationAcknowledgements<br/>Zero payment or financial side-effect
    DB-->>DeliveryBoy: Read/seen receipt recorded
```

### 4.1 Idempotency and Transaction Specification
The notification procedure `usp_Notifications_EmitBillPublished` in `sql/11_FinalSystemIntegration.sql` enforces strict idempotency:
```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_EmitBillPublished]
    @BillId BIGINT,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Verify bill exists and is published
    DECLARE @CustomerId INT, @InvoiceAmount DECIMAL(18,2), @BalanceAmount DECIMAL(18,2);
    DECLARE @BillingMonth TINYINT, @BillingYear SMALLINT;
    SELECT 
        @CustomerId = b.CustomerId,
        @InvoiceAmount = b.CurrentMonthInvoiceAmount,
        @BalanceAmount = b.BalanceAmount,
        @BillingMonth = b.BillingMonth,
        @BillingYear = b.BillingYear
    FROM dbo.MonthlyBills b
    WHERE b.BillId = @BillId AND b.IsPublished = 1;

    IF @CustomerId IS NULL
        RETURN; -- Bill not published or does not exist

    -- 2. Enforce strict idempotency: check if publication notice already emitted
    IF EXISTS (
        SELECT 1 FROM dbo.Notifications 
        WHERE BillId = @BillId AND NotificationType = 'BillPublished'
    )
    BEGIN
        RETURN; -- Skip: notification already exists
    END;

    -- 3. Resolve customer, route, and assigned delivery boy
    DECLARE @CustomerName NVARCHAR(100), @RouteId INT, @DeliveryBoyId INT, @RouteName NVARCHAR(100);
    SELECT 
        @CustomerName = c.FullName,
        @RouteId = c.RouteId,
        @DeliveryBoyId = c.CurrentDeliveryBoyId,
        @RouteName = r.RouteName
    FROM dbo.Customers c
    JOIN dbo.Routes r ON c.RouteId = r.RouteId
    WHERE c.CustomerId = @CustomerId;

    DECLARE @MonthYearStr VARCHAR(10) = RIGHT('0' + CAST(@BillingMonth AS VARCHAR(2)), 2) + '-' + CAST(@BillingYear AS VARCHAR(4));
    DECLARE @NoticeTitle NVARCHAR(100) = N'Bill Published: ' + @CustomerName;
    DECLARE @NoticeMessage NVARCHAR(1000) = N'Monthly bill for ' + @CustomerName + N' (' + @MonthYearStr + N') published on ' + @RouteName + N'. Amount: ₹' + CAST(@InvoiceAmount AS NVARCHAR(20)) + N'.';

    -- 4. Insert notification targeting the route and assigned delivery boy
    INSERT INTO dbo.Notifications (
        Title, Message, NotificationType, TargetRole, TargetRouteId, TargetDeliveryBoyId,
        CustomerId, BillId, CustomerNameSnapshot, UpdatedBillAmount, NewTotalOutstandingBalance,
        BillingMonthYear, PendingMonthsCount, CreatedAt, CreatedBy
    ) VALUES (
        @NoticeTitle, @NoticeMessage, 'BillPublished', 'DeliveryBoy', @RouteId, @DeliveryBoyId,
        @CustomerId, @BillId, @CustomerName, @InvoiceAmount, @BalanceAmount,
        @MonthYearStr, 1, SYSUTCDATETIME(), @AdminUserId
    );
END;
```

---

## 5. Reporting Contracts

All reporting contracts reuse existing approved financial calculation rules without introducing divergent logic.

### 5.1 Monthly Billing vs. Actual Collection Trends
- **Endpoint:** `GET /Reports/BillingVsCollections?months=6`
- **Method:** `IReportsService.GetBillingVsCollectionTrendsAsync(int months, CancellationToken ct)`
- **SQL Source:** Aggregates published `dbo.MonthlyBills` (`SUM(CurrentMonthInvoiceAmount)`) and valid non-voided `dbo.Payments` (`SUM(Amount)`) grouped by calendar `BillingYear` and `BillingMonth`.
- **Contract Model:**
  ```csharp
  public class MonthlyBillingVsCollectionTrendDto
  {
      public short BillingYear { get; set; }
      public byte BillingMonth { get; set; }
      public string MonthName { get; set; } = string.Empty;
      public decimal InvoicedAmount { get; set; }
      public decimal CollectedAmount { get; set; }
      public decimal CollectionEfficiencyPercent => InvoicedAmount > 0 ? Math.Round((CollectedAmount / InvoicedAmount) * 100, 2) : 0m;
  }
  ```

### 5.2 Route-Wise Milk Consumption
- **Endpoint:** `GET /Reports/RouteMilkConsumption?fromDate=...&toDate=...`
- **Method:** `IReportsService.GetRouteMilkConsumptionAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct)`
- **SQL Source:** Aggregates `dbo.DailyMilkDeliveries` where `Status = 'Completed'` and `EntryDate BETWEEN @FromDate AND @ToDate`, grouped by `RouteId` and `RouteName`.
- **Contract Model:**
  ```csharp
  public class RouteMilkConsumptionReportDto
  {
      public int RouteId { get; set; }
      public string RouteName { get; set; } = string.Empty;
      public int ActiveCustomerCount { get; set; }
      public decimal TotalLiquidVolumeLitres { get; set; }
      public decimal MorningVolumeLitres { get; set; }
      public decimal EveningVolumeLitres { get; set; }
  }
  ```

### 5.3 Daily Milk Consumption by Category and Shift
- **Endpoint:** `GET /Reports/CategoryShiftConsumption?fromDate=...&toDate=...&milkCategoryId=...&shift=...`
- **Method:** `IReportsService.GetCategoryShiftConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? milkCategoryId, string? shift, CancellationToken ct)`
- **SQL Source:** Joins `dbo.DailyMilkDeliveries`, `dbo.DailyMilkDeliveryItems`, and `dbo.MilkCategories` where `Status = 'Completed'`, grouped by `EntryDate`, `CategoryName`, `Shift`, and `Unit`.
- **Contract Model:**
  ```csharp
  public class DailyCategoryShiftConsumptionDto
  {
      public DateOnly EntryDate { get; set; }
      public string CategoryName { get; set; } = string.Empty;
      public string Shift { get; set; } = string.Empty;
      public string Unit { get; set; } = string.Empty;
      public decimal DeliveredQuantity { get; set; }
      public decimal TotalMonetaryAmount { get; set; }
  }
  ```

### 5.4 Customer Outstanding Balance Ageing (Approved Month-Based Contract)
- **Endpoint:** `GET /Reports/CustomerBalanceAging?routeId=...`
- **Method:** `IReportsService.GetCustomerBalanceAgingAsync(int? routeId, CancellationToken ct)`
- **SQL Source:** Evaluates published `dbo.MonthlyBills` with `BalanceAmount > 0`. Ageing is calculated relative to calendar billing cycles using **unpaid invoice portions** (`BalanceAmount`):
  - **1 Month:** Invoices from previous closed billing month
  - **2 Months:** Invoices from 2 billing months prior
  - **3 Months:** Invoices from 3 billing months prior
  - **4–5 Months:** Invoices from 4 to 5 billing months prior
  - **6+ Months:** Invoices from 6 or more billing months prior
- **Contract Model:**
  ```csharp
  public class CustomerBalanceAgingDto
  {
      public int CustomerId { get; set; }
      public string CustomerCode { get; set; } = string.Empty;
      public string FullName { get; set; } = string.Empty;
      public string Phone { get; set; } = string.Empty;
      public string RouteName { get; set; } = string.Empty;
      public decimal UnpaidBalance1Month { get; set; }
      public decimal UnpaidBalance2Months { get; set; }
      public decimal UnpaidBalance3Months { get; set; }
      public decimal UnpaidBalance4To5Months { get; set; }
      public decimal UnpaidBalance6PlusMonths { get; set; }
      public decimal TotalOutstanding { get; set; }
      public int UnpaidBillsCount { get; set; }
  }
  ```

### 5.5 Delivery Boy Performance & Compensation Summary
- **Endpoint:** `GET /Reports/CompensationSummary?month=...&year=...`
- **Method:** Reuses existing `ICompensationService.GetMonthlyCompensationListAsync(criteria, ct)`.
- **Contract Model:** Reuses existing `MonthlyCompensationListItemDto`.

---

## 6. Export Contracts & Data Access Mechanisms

All data exported through the Export Management Console is retrieved via dedicated repository queries executing under `db_dms_app` permissions and streamed directly into RFC 4180-compliant CSV format.

### 6.1 Export Endpoints & Repository Signatures
```csharp
public interface IExportRepository
{
    Task<IReadOnlyList<CustomerExportItemDto>> GetCustomersExportAsync(int? routeId, bool? isActive, CancellationToken ct = default);
    Task<IReadOnlyList<DailyDeliveryExportItemDto>> GetDailyDeliveriesExportAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyBillExportItemDto>> GetMonthlyBillsExportAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentExportItemDto>> GetPaymentsExportAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default);
    Task<IReadOnlyList<CompensationExportItemDto>> GetCompensationExportAsync(byte month, short year, CancellationToken ct = default);
}

public interface IExportService
{
    Task<byte[]> GenerateCustomersCsvAsync(int? routeId, bool? isActive, CancellationToken ct = default);
    Task<byte[]> GenerateDailyDeliveriesCsvAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default);
    Task<byte[]> GenerateMonthlyBillsCsvAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default);
    Task<byte[]> GeneratePaymentsCsvAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default);
    Task<byte[]> GenerateCompensationCsvAsync(byte month, short year, CancellationToken ct = default);
}
```

### 6.2 Export Procedures in `sql/11_FinalSystemIntegration.sql`
- `usp_Export_GetCustomers` (`@RouteId INT = NULL, @IsActive BIT = NULL`)
- `usp_Export_GetDailyDeliveries` (`@FromDate DATE, @ToDate DATE, @RouteId INT = NULL, @Shift NVARCHAR(20) = NULL`)
- `usp_Export_GetMonthlyBills` (`@Month TINYINT, @Year SMALLINT, @PaymentStatus NVARCHAR(20) = NULL, @RouteId INT = NULL`)
- `usp_Export_GetPayments` (`@FromDate DATE = NULL, @ToDate DATE = NULL, @PaymentMode NVARCHAR(20) = NULL`)
- `usp_Export_GetCompensation` (`@Month TINYINT, @Year SMALLINT`)

### 6.3 RFC 4180 CSV Formatting Helper
Kevin will implement `CsvExportHelper` writing RFC 4180 compliant streams:
- Fields with commas, double quotes, carriage returns, or newlines are enclosed in double quotes.
- Inner double quotes are escaped as `""`.
- UTF-8 encoding with byte order mark (`Encoding.UTF8.GetPreamble()`) ensures seamless opening in Microsoft Excel and external spreadsheet tools.

---

## 7. Delivery Boy Identity Resolution & Operational Roster

### 7.1 Identity & Route Resolution Pipeline
When an authenticated delivery boy accesses portal endpoints, their identity is resolved through a single query:
```sql
SELECT 
    db.DeliveryBoyId,
    db.UserId,
    db.FullName,
    db.PrimaryRouteId,
    r.RouteName,
    db.CompensationType,
    db.BaseSalary,
    db.IsActive
FROM dbo.DeliveryBoys db
LEFT JOIN dbo.Routes r ON db.PrimaryRouteId = r.RouteId
WHERE db.UserId = @UserId AND db.IsActive = 1;
```
If no active record is found, the system throws `UnauthorizedAccessException`, cleanly redirecting to `/Auth/DeliveryBoyLogin`.

### 7.2 My Customers Roster Contract
- **Endpoint:** `GET /DailyMilk/MyCustomers`
- **Method:** `IDeliveryBoyService.GetAssignedCustomersRosterAsync(int deliveryBoyId, CancellationToken ct)`
- **SQL Logic:** Queries customers where `CurrentDeliveryBoyId = @DeliveryBoyId AND IsActive = 1`, ordered by `DeliverySequence ASC`.
- **Roster ViewModel:**
  ```csharp
  public class DeliveryBoyCustomerRosterItemViewModel
  {
      public int CustomerId { get; set; }
      public string CustomerCode { get; set; } = string.Empty;
      public string FullName { get; set; } = string.Empty;
      public string Phone { get; set; } = string.Empty;
      public string Address { get; set; } = string.Empty;
      public string Area { get; set; } = string.Empty;
      public string Landmark { get; set; } = string.Empty;
      public int DeliverySequence { get; set; }
      public List<CustomerSubscriptionSummaryDto> Subscriptions { get; set; } = new();
      public bool IsOnVacationToday { get; set; }
  }
  ```

### 7.3 Delivery Boy Dashboard Metrics (`usp_Dashboard_GetDeliveryBoyMetrics`)
- **Assigned Route:** Route Name and Route ID.
- **Route Customers:** Total active customers on assigned route.
- **Today's Delivered Litres:** Sum of completed liquid milk litres delivered today by this delivery boy.
- **Completed Drops Today:** Count of completed delivery entries today.
- **Pending Drops Today:** Count of pending delivery entries today.
- **Unread Route Notifications:** Count of unacknowledged notifications targeting this delivery boy or route.

---

## 8. System Settings & Configuration

### 8.1 Separation of Settings Classes
1. **Existing Database Schema:** Table `dbo.AppSettings` exists in `sql/03_CreateTables.sql` (`SettingId`, `SettingKey`, `SettingValue`, `DataType`, `Description`, `UpdatedAt`, `UpdatedBy`).
2. **Configuration Display:**
   - Supported enterprise keys: `DairyName`, `ContactPhone`, `ContactEmail`, `OperatingAddress`, `DefaultCurrency`, `MorningShiftCutoff`, `EveningShiftCutoff`, `SystemAnnouncement`.
   - If a setting has not been configured in the database by an administrator, the UI renders a neutral placeholder (e.g. `"Not Configured"` / blank).
   - No fabricated phone numbers, emails, addresses, or dairy names are seeded into SQL.
3. **Strict Invariant Restrictions:**
   - **NO** automatic payment due date setting (`BillingCycleDueDays` is prohibited).
   - **NO** setting can alter historical, published, or calculated invoice balances.
   - All settings modifications must be audited in `dbo.AuditLogs` with `OldValuesJson` and `NewValuesJson`.

---

## 9. Kevin's Consolidated Backend Handoff (SQL & ADO.NET)

**Assigned Engineer:** Kevin Gajera (`kevingajera04`)  
**Scope:** SQL Scripts, Models, DTOs, Repositories, Application Services, Dependency Injection  
**Strict Rule:** Rely strictly on assigned backend files; do not modify controllers, Razor views, or completed Day 1–6 functionality.

### 9.1 Files to Create or Modify
```text
sql/
  └── 11_FinalSystemIntegration.sql               [NEW]
Models/
  ├── Dashboard/
  │   ├── AdminDashboardViewModel.cs              [NEW]
  │   └── DeliveryBoyDashboardViewModel.cs        [NEW]
  ├── Reports/
  │   ├── MonthlyBillingVsCollectionTrendDto.cs   [NEW]
  │   ├── RouteMilkConsumptionReportDto.cs        [NEW]
  │   ├── DailyCategoryShiftConsumptionDto.cs     [NEW]
  │   └── CustomerBalanceAgingDto.cs              [NEW - Month-based buckets]
  ├── Export/
  │   ├── CustomerExportItemDto.cs                [NEW]
  │   ├── DailyDeliveryExportItemDto.cs           [NEW]
  │   ├── MonthlyBillExportItemDto.cs             [NEW]
  │   ├── PaymentExportItemDto.cs                 [NEW]
  │   └── CompensationExportItemDto.cs            [NEW]
  ├── Notification/
  │   ├── Notification.cs                         [NEW]
  │   ├── NotificationAcknowledgement.cs          [NEW]
  │   ├── NotificationListItemDto.cs              [NEW]
  │   └── CreateNotificationViewModel.cs          [NEW]
  └── Settings/
      ├── AppSettingItemModel.cs                  [NEW]
      └── SystemSettingsViewModel.cs              [NEW]
Common/
  └── CsvExportHelper.cs                          [NEW - RFC 4180 Escaping Utility]
Repositories/
  ├── IDashboardRepository.cs                     [NEW]
  ├── DashboardRepository.cs                      [NEW]
  ├── IReportsRepository.cs                       [NEW]
  ├── ReportsRepository.cs                        [NEW]
  ├── IExportRepository.cs                        [NEW]
  ├── ExportRepository.cs                         [NEW]
  ├── INotificationRepository.cs                  [NEW]
  ├── NotificationRepository.cs                   [NEW]
  ├── ISettingsRepository.cs                      [NEW]
  └── SettingsRepository.cs                       [NEW]
Services/
  ├── IDashboardService.cs                        [NEW]
  ├── DashboardService.cs                         [NEW]
  ├── IReportsService.cs                          [NEW]
  ├── ReportsService.cs                           [NEW]
  ├── IExportService.cs                           [NEW]
  ├── ExportService.cs                            [NEW]
  ├── INotificationService.cs                     [NEW]
  ├── NotificationService.cs                      [NEW]
  ├── ISettingsService.cs                         [NEW]
  ├── SettingsService.cs                          [NEW]
  └── BillingService.cs                           [MODIFIED - Wire EmitBillPublishedNotificationAsync]
Program.cs                                        [MODIFIED - Register DI services]
```

### 9.2 Dependency Injection Registrations (`Program.cs`)
```csharp
// Day 7 System Integration Registrations
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
builder.Services.AddScoped<IReportsService, ReportsService>();

builder.Services.AddScoped<IExportRepository, ExportRepository>();
builder.Services.AddScoped<IExportService, ExportService>();

builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
```

---

## 10. Viraj's Consolidated Frontend Handoff (MVC & Razor)

**Assigned Engineer:** Viraj Bodar (`virajbodar21`)  
**Scope:** MVC Controllers, ViewModels, Razor Views, Layout Navigation, Topbar Badges, Client Scripting  
**Strict Rule:** Rely strictly on Kevin's approved interfaces; do not modify SQL or database scripts.

### 10.1 Files to Create or Modify
```text
Controllers/
  ├── HomeController.cs                           [MODIFIED - Bind real viewmodels for Index, Privacy, Error]
  ├── ReportsController.cs                        [NEW - [Authorize(Roles = "Admin")]]
  ├── ExportController.cs                         [NEW - [Authorize(Roles = "Admin")]]
  ├── NotificationController.cs                   [NEW - Role-aware broadcast and delivery boy alert endpoints]
  ├── SettingsController.cs                       [NEW - Admin enterprise settings and delivery boy preferences]
  ├── HelpSupportController.cs                    [NEW - [Authorize]]
  └── DailyMilkController.cs                      [MODIFIED - Add MyCustomers GET action for DeliveryBoy]
Views/
  ├── Home/
  │   ├── Index.cshtml                            [OVERHAULED - Real Admin Dashboard KPIs, Table, Chart]
  │   └── Privacy.cshtml                          [OVERHAULED - Real Delivery Boy Dashboard, Metrics, Popup]
  ├── Reports/
  │   ├── Index.cshtml                            [NEW - Unified Analytics Hub & Chart.js Charts]
  │   ├── DailyMilk.cshtml                        [NEW - Daily Milk Consumption Report]
  │   └── CustomerAging.cshtml                    [NEW - Month-based Customer Outstanding Aging Register]
  ├── Export/
  │   └── Index.cshtml                            [NEW - Centralized Export Hub with 5 Download Cards]
  ├── Notification/
  │   ├── Index.cshtml                            [NEW - Admin Broadcast Console & Audit Table]
  │   └── DeliveryBoyNotifications.cshtml         [NEW - Mobile Delivery Boy Alerts with AJAX Ack]
  ├── Settings/
  │   ├── Index.cshtml                            [NEW - Admin Enterprise Settings with neutral defaults]
  │   ├── DeliveryBoySettings.cshtml              [NEW - Delivery Boy Preferences & Language]
  │   └── Profile.cshtml                          [NEW - Account Details & Payout Summary]
  ├── HelpSupport/
  │   └── Index.cshtml                            [NEW - Role-Aware Operational Manuals]
  ├── DailyMilk/
  │   └── MyCustomers.cshtml                      [NEW - Delivery Boy Route Customer Roster View]
  └── Shared/
      ├── _LayoutAdmin.cshtml                     [MODIFIED - Wire 10 menu items & bell badge]
      ├── _LayoutDeliveryBoy.cshtml               [MODIFIED - Wire 8 menu items & bell badge]
      └── Error.cshtml                            [MODIFIED - CoreUI styled status code error page]
Program.cs                                        [MODIFIED - Update DeliveryBoyBasePaths & StatusCodePages]
```

### 10.2 Route Classifier & Error Middleware Configuration
In `Program.cs`:
```csharp
private static readonly string[] DeliveryBoyBasePaths =
{
    "/Home/Privacy",
    "/DailyMilk",
    "/Notification/DeliveryBoyNotifications",
    "/Settings/DeliveryBoySettings",
    "/Settings/Profile"
};
```
And status code page execution:
```csharp
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
```

---

## 11. Final Testing & Submission-Readiness Checklist

Before final submission, the entire engineering team will verify:

### Layer A: Automated C# Code & Verification Suite
- [ ] `dotnet build --no-incremental` compiles with **0 warnings and 0 errors**.
- [ ] No missing DI registrations in `Program.cs`.
- [ ] All 10 admin navigation links in `_LayoutAdmin.cshtml` resolve to active routes (zero `#` dead links).
- [ ] All 8 delivery boy navigation links in `_LayoutDeliveryBoy.cshtml` resolve to active routes (zero `#` dead links).
- [ ] All POST endpoints are protected by `[ValidateAntiForgeryToken]`.
- [ ] All admin actions are protected by `[Authorize(Roles = "Admin")]`.
- [ ] All delivery boy actions are protected by `[Authorize(Roles = "DeliveryBoy")]`.

### Layer B: SQL Server Database Integration & Constraint Testing
- [ ] `sql/11_FinalSystemIntegration.sql` executes cleanly without errors.
- [ ] `usp_Dashboard_GetAdminMetrics` returns 3 valid result sets with live data.
- [ ] `usp_Dashboard_GetDeliveryBoyMetrics` returns correct route and drop metrics.
- [ ] `usp_Notifications_EmitBillPublished` enforces idempotency (no duplicate alerts for same bill).
- [ ] `usp_Notifications_Acknowledge` records read receipt without altering financial tables.
- [ ] All new procedures are granted `EXECUTE` permission to `db_dms_app`.

### Layer C: End-to-End Browser & Role Authorization Verification
- [ ] **Admin Login:** Logs into `/Home/Index`. Dashboard renders live KPI counts, today's deliveries table, and quick actions.
- [ ] **Delivery Boy Login:** Logs into `/Home/Privacy`. Portal renders assigned route name, customer count, drop metrics, unread alert count, and triggers the one-time bill popup modal when unacknowledged publication notices exist.
- [ ] **My Customers:** Delivery boy views `/DailyMilk/MyCustomers`. Roster renders assigned customers with delivery sequence, milk categories, and today's vacation status.
- [ ] **Reports Hub:** Navigates to `/Reports`. Chart.js renders billing trend and milk distribution charts. Customer aging report renders approved month-based buckets (`1 mo`, `2 mo`, `3 mo`, `4–5 mo`, `6+ mo`).
- [ ] **Export Hub:** Navigates to `/Export`. Downloading Customers, Deliveries, and Bills produces valid RFC 4180 CSV files.
- [ ] **Notifications Flow:**
  1. Admin publishes a notice or bill.
  2. Delivery Boy topbar bell shows unread count `(1)`.
  3. Delivery Boy views `/Notification/DeliveryBoyNotifications` and clicks `"Acknowledge"`.
  4. Badge updates to `"Acknowledged"` and unread counter decrements to `0`.
- [ ] **Settings:** Admin updates Dairy Name; updated value persists in `dbo.AppSettings` and reflects in layout brand header. Unset settings display neutral placeholders.
- [ ] **Help & Support:** Both roles can access operational guides with appropriate content.
- [ ] **Error Handling:** Navigating to an invalid URL (`/InvalidPage123`) renders the polished 404 page rather than an unstyled exception.

---

## 12. Confirmation of Baseline & Day 1–6 Architectural Integrity

- **Git Baseline:** Remains strictly at `4f1005b01b680133cf45ffaecb38be7287beef8e` on `main`.
- **Zero Incompatible Changes:** All previously approved architecture, domain models, stored procedures, repositories, and services from Days 1 through 6 remain completely intact and unchanged.

---
*End of Day 7 Final Architecture & System Integration Handoff Document (v1.2.0).*
