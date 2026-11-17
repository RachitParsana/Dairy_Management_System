# Dairy Management System — Customer Management
## Lifecycle, Business Rules, and Backend Design Specification

**Document Version:** 1.3.0 (Day 3 Final P1 Corrections: Assignment Correction & Deactivation Semantics Applied)
**Phase:** Day 3 Architecture & Backend Specification
**Lead Architect & Author:** Rachit (Lead Architect & Backend Developer)
**Collaborators:** Kevin (Database Engineer), Viraj (Frontend Engineer)
**Status:** Approved Architecture Baseline with Database-Integrity Specifications

---

## 1. Customer Lifecycle Architecture

In the Dairy Management System, **Customers** represent the residential and commercial subscribers who consume dairy products on recurring or customized schedules across designated distribution routes.

### Core Architectural Principles
1. **Passive Master Record Model:** Customers do not have web login accounts or self-service portals in the current release. They are passive relational entities administered strictly by the **Admin** and read-accessed by assigned **Delivery Boys**.
2. **Multi-Category Subscription Support:** A customer is never constrained to a single milk type or a single price. A customer may simultaneously subscribe to Cow Milk, Buffalo Milk, and curd/paneer products across Morning and/or Evening shifts with independent quantities and custom unit price overrides.
3. **Strict Immutability of Historical Accounting:** Customer profile updates, delivery boy reassignments, subscription modifications, and price adjustments must **never** retroactively modify historical delivery records, published monthly bills, payment receipts, or audit logs.
4. **Permanent Relational Integrity (No Hard Deletes):** Physical SQL `DELETE` operations on customer master records are strictly forbidden. Customer deactivation is implemented via status transitions and soft deletion (`IsActive = 0`), preserving all historical invoices, payments, and delivery runsheets.
5. **Effective-Dated Continuity:** Pricing, subscriptions, and delivery boy assignments utilize effective-dated intervals (`EffectiveFrom` to `EffectiveTo`, `AssignedFrom` to `AssignedTo`) to guarantee a deterministic audit trail for any given calendar date.

---

## 2. Customer States and Transitions

The customer lifecycle is governed by an explicit finite state machine with four operational states:

```
                  +----------------------------------------------+
                  |                                              |
                  v                                              | Reactivation
            [  ACTIVE  ] <=======> [ ON VACATION ]               | (Requires route,
                  |        (Approved      |                      |  delivery boy &
                  |        Vacation)      |                      |  subscription)
                  |                       |                      |
                  +-----------------------+                      |
                             |                                   |
                             | Deactivation                      |
                             v                                   |
                       [ INACTIVE ] -----------------------------+
```

### 2.1 State Definitions

| State | Master Column Status | `IsActive` | Operational Meaning | Delivery & Billing Implication |
|---|---|---|---|---|
| **Active** | `'Active'` | `1` | Customer is in good standing and receiving daily dairy deliveries per active subscriptions. | Included in delivery runsheets; deliveries charged daily; included in monthly billing runs. |
| **On Vacation** | `'Active'` (with active approved vacation) | `1` | Customer has an approved `CustomerVacations` record covering the current business date and shift. | Locked out on delivery runsheets (rendered as disabled with 0.00 quantity); zero charges applied. |
| **Inactive** | `'Inactive'` | `0` | Customer service has been discontinued (e.g., relocation, termination). | Excluded from delivery runsheets; no deliveries generated; past unpaid bills remain active and payable. |
| **Reactivated** | `'Active'` | `1` | Previously inactive customer whose service has been officially resumed by Admin. | Requires new delivery boy assignment and verified subscription lines before runsheet inclusion. |

### 2.2 State Transition Invariants

1. **Active → On Vacation:**
   - Evaluated dynamically when an Admin approves a vacation request in `CustomerVacations` whose date window includes the target date and shift.
   - Master customer record remains `IsActive = 1` and `Status = 'Active'`. The "On Vacation" state is an operational overlay.
   - Deliveries for dates within the approved window are recorded as `Not Delivered` with packaging quantity `0.000` and zero financial charge.

2. **On Vacation → Active:**
   - Occurs automatically when the calendar date advances past `CustomerVacations.EndDate`, OR when an Admin executes an **Early Resumption** (`ResumedDate` set).
   - Normal delivery quantities and charges resume on the resumption date.

3. **Active → Inactive (Deactivation):**
   - Executed solely by Admin through `POST /Customer/Deactivate`.
   - **Standardized Effective Date Invariant:** Deactivation is effective from the selected calendar date $D$. No new delivery or milk charge is permitted on or after $D$.
   - The last eligible delivery date is $D - 1$.
   - Active delivery boy assignment, active subscriptions, and active custom pricing end on $D - 1$.
   - If a delivery has already been recorded on date $D$: Admin is prompted with an explicit confirmation modal. Upon confirmation, today's recorded delivery is billed, and service termination takes effect on $D + 1$ (closing records on date $D$).
   - If an assignment or subscription started on date $D$: Handled via audited same-day cancellation/void protocol (`IsActive = 0`, end dates clamped to $D$) to strictly satisfy SQL CHECK constraints (`AssignedTo >= AssignedFrom`, `EffectiveTo >= EffectiveFrom`).
   - Master record updated to `Status = 'Inactive'` and `IsActive = 0`.
   - **Crucial Ledger Invariant:** Unpaid published `MonthlyBills` remain completely intact. Historical payments and deliveries are never deleted. Outstanding balances remain collectible on the Aged Debtors ledger.

4. **Inactive → Active (Reactivation):**
   - Executed solely by Admin through `POST /Customer/Reactivate`.
   - Service resumes on designated calendar date $R$ ($R \ge \text{Today}$). First eligible delivery date is $R$.
   - Historical closed subscriptions and past delivery boy assignments remain closed and immutable.
   - Reactivation creates **new** subscription records (new surrogate `SubscriptionId` keys) and a **new** assignment record starting on $R$.
   - Master record updated to `Status = 'Active'` and `IsActive = 1`.
   - Historical bills and payment ledgers remain linked seamlessly via `CustomerId`.

5. **Customer Deletion (Hard Delete):**
   - **Strict Invariant:** Hard `DELETE FROM Customers` is permanently blocked by foreign key constraints (`ON DELETE NO ACTION` on `DailyMilkDeliveries`, `MonthlyBills`, `Payments`, `CustomerSubscriptions`, etc.).
   - In UI and API layers, "Delete" is mapped directly to **Deactivation** (`IsActive = 0`).

---

## 3. Customer Profile Specification

A complete customer profile comprises basic demographic information, delivery logistics, and audit metadata.

### 3.1 Field Specifications

| Field Name | Type | Nullable | Validation Rules | Description |
|---|---|---|---|---|
| `CustomerId` | `INT` | No | System generated IDENTITY (PK). | Surrogate primary key. |
| `CustomerCode` | `VARCHAR(20)` | No | `UQ`, format `CUST-XXXXX` (e.g., `CUST-00104`). | Unique business identifier printed on bills and receipts. |
| `FullName` | `NVARCHAR(100)` | No | Min 2, Max 100 chars, letters/spaces/dots. | Customer English name. |
| `FullNameGujarati` | `NVARCHAR(100)` | Yes | Max 100 chars, Unicode Gujarati text. | Customer name in Gujarati script for localized delivery runsheets. |
| `Phone` | `VARCHAR(15)` | No | 10 to 15 digits, optional leading `+`. | Primary contact mobile number. Used for SMS/notifications. |
| `AlternatePhone` | `VARCHAR(15)` | Yes | 10 to 15 digits, optional leading `+`. | Secondary emergency contact or family member number. |
| `Address` | `NVARCHAR(250)` | No | Min 5, Max 250 chars. | Physical house number, building/flat name, society. |
| `Area` | `NVARCHAR(100)` | Yes | Max 100 chars. | Neighborhood, sector, or locality within the city. |
| `Landmark` | `NVARCHAR(100)` | Yes | Max 100 chars. | Nearby landmark for delivery boy navigation. |
| `RouteId` | `INT` | No | Must exist in `dbo.Routes(RouteId)`. | Territorial route covering the customer's address. |
| `CurrentDeliveryBoyId`| `INT` | Yes | Must exist in `dbo.DeliveryBoys(DeliveryBoyId)`. | Active assigned delivery boy (`NULL` if unassigned). |
| `DeliverySequence` | `INT` | No | Integer `>= 0`, default `0`. | Drop-off sequence number along the delivery boy's run. |
| `PreferredShift` | `NVARCHAR(20)` | No | `'Morning'`, `'Evening'`, or `'Both'`. | Overall customer delivery window preference. |
| `Status` | `NVARCHAR(20)` | No | `'Active'`, `'Suspended'`, or `'Inactive'`. | Master lifecycle status. |
| `Notes` | `NVARCHAR(500)` | Yes | Max 500 chars. | Special delivery instructions (e.g., "Leave at gate"). |
| `DeliveryStartDate` | `DATE` | No | Business date deliveries begin. | Cannot be more than 30 days in the past. |
| `IsActive` | `BIT` | No | Default `1`. | Soft deletion flag. |
| `CreatedAt` | `DATETIME2(7)` | No | `SYSUTCDATETIME()`. | UTC audit creation timestamp. |
| `CreatedBy` | `INT` | No | FK to `dbo.Users(UserId)`. | User ID of Admin who created the customer. |
| `UpdatedAt` | `DATETIME2(7)` | Yes | UTC timestamp of last update. | Updated automatically via repository. |
| `UpdatedBy` | `INT` | Yes | FK to `dbo.Users(UserId)`. | User ID of Admin who last modified the customer. |

---

## 4. Multi-Category Milk Configuration and Subscriptions

Real-world dairy customers frequently purchase multiple products simultaneously (e.g., 2 Litres of Cow Milk every Morning, 1 Litre of Buffalo Milk every Evening, and 0.5 Kg of Curd on Wednesdays and Saturdays). The system must never restrict a customer to a single milk category.

### 4.1 Subscription Architecture

Subscriptions are modeled in `dbo.CustomerSubscriptions` as independent category line items:
- Each subscription links a `CustomerId` to a `CategoryId`.
- Each line specifies:
  - `Shift`: `'Morning'`, `'Evening'`, or `'Both'`.
  - `PackagingQuantity`: Quantity in retail units (e.g., `2.000` Litres or `1.000` Kg).
  - `DailyLiquidVolumeLitres`: Derived liquid volume used for delivery vehicle capacity planning and delivery boy per-litre commission calculations (`PackagingQuantity * LitreEquivalent`). For non-liquid items (paneer, curd), `DailyLiquidVolumeLitres = 0.000`.
  - `EffectiveFrom` and `EffectiveTo`: Date interval during which the subscription line is active.
  - `DeliverySchedule`: Recurrence frequency (`Daily`, `Weekdays`, `Weekends`, `CustomDays`).
  - `DeliveryDays`: Canonical list of active ISO weekdays (`1` = Monday, `7` = Sunday).

### 4.2 Subscription Rules

1. **Shift Distinction:** A customer may have distinct subscriptions for the same category across shifts (e.g., 1.5L Morning and 0.5L Evening).
2. **Mutual Exclusivity:** A customer cannot have overlapping active subscriptions for the same Category and Shift.
   - Filtered unique index: `(CustomerId, CategoryId, Shift) WHERE IsActive = 1 AND EffectiveTo IS NULL`.
3. **Quantity Non-Zero:** `PackagingQuantity` must strictly be greater than zero (`> 0.000`). If a customer pauses a category indefinitely, the subscription row is ended (`EffectiveTo = Yesterday`, `IsActive = 0`).
4. **Subscription Change Workflow:** When an Admin alters a customer's daily quantity starting on a future or current date:
   - The existing active subscription line is updated with `EffectiveTo = NewEffectiveFrom - 1 day`.
   - A new subscription line is inserted with `EffectiveFrom = NewEffectiveFrom`, `EffectiveTo = NULL`, `IsActive = 1`, and the new quantity.
   - Historical deliveries generated prior to `NewEffectiveFrom` remain completely untouched.

---

## 5. Delivery Schedule Ownership & Granular Weekday Rules

### 5.1 Ownership Hierarchy
**The delivery schedule is strictly owned at the SUBSCRIPTION LEVEL (`CustomerSubscriptions`), not the customer level.**

#### Architectural Rationale:
1. **Category Independence:** Different dairy products have fundamentally different delivery rhythms. For example:
   - *Cow Milk:* Consumed daily (7 days a week: `1,2,3,4,5,6,7`).
   - *Buffalo Milk:* Weekdays only (`1,2,3,4,5`).
   - *Fresh Paneer / Curd:* Weekend consumption only (`6,7` - Saturday & Sunday).
2. **Shift Independence:** A customer may take morning milk daily (7 days), but evening milk only on weekdays (5 days).
3. **Customer-Level Role:** `Customers.PreferredShift` defines the household's overarching delivery window capability (`Morning`, `Evening`, `Both`), acting as an outer boundary. The individual subscription lines dictate the exact product-shift delivery schedule.

### 5.2 Subscription Schedule Models & Valid Combinations

| Schedule Type | Database `DeliverySchedule` | Required `DeliveryDays` Value | Operational Meaning |
|---|---|---|---|
| **Daily** | `'Daily'` | `'1,2,3,4,5,6,7'` | Delivered every single calendar day of the week. |
| **Weekdays Only** | `'Weekdays'` | `'1,2,3,4,5'` | Delivered Monday through Friday; excluded on Saturday and Sunday. |
| **Weekends Only** | `'Weekends'` | `'6,7'` | Delivered Saturday and Sunday only. |
| **Custom Days** | `'CustomDays'` | Any non-empty unique combination of weekdays `1..7` (e.g. `'1,3,5'`). | Delivered strictly on specified unique ISO weekday numbers. |

### 5.3 Schedule Lifecycle Scenarios

1. **Pausing a Subscription:**
   - When a customer pauses an individual subscription line (e.g., pauses Curd indefinitely while continuing Milk):
   - The subscription row has its `EffectiveTo` set to `PauseDate - 1 day` and `IsActive = 0`.
   - The category is immediately excluded from future runsheets and billing without altering the customer's other active products.
2. **Mid-Month Schedule Change:**
   - E.g., On Nov 16th, customer switches from Daily Cow Milk to Mon-Wed-Fri Cow Milk:
   - Active row 1 (Daily): `EffectiveTo = '2026-11-15'`.
   - Active row 2 (CustomDays `1,3,5`): `EffectiveFrom = '2026-11-16'`, `EffectiveTo = NULL`.
   - November 1–15 deliveries remain calculated on the daily schedule; November 16–30 deliveries follow `1,3,5`. Past published or draft records are preserved.

### 5.4 Database Integrity & Validation Strategy for Delivery Days

#### A. Evaluation of Normalized Child Table vs. String Storage
1. **Option 1: Normalized Child Table (`dbo.CustomerSubscriptionDeliveryDays`)**
   ```sql
   CREATE TABLE [dbo].[CustomerSubscriptionDeliveryDays] (
       [SubscriptionId] INT NOT NULL,
       [DayOfWeek] TINYINT NOT NULL, -- 1=Monday ... 7=Sunday (ISO 8601)
       CONSTRAINT [PK_CustomerSubscriptionDeliveryDays] PRIMARY KEY CLUSTERED ([SubscriptionId], [DayOfWeek]),
       CONSTRAINT [FK_SubscriptionDeliveryDays_Subscription] FOREIGN KEY ([SubscriptionId])
           REFERENCES [dbo].[CustomerSubscriptions]([SubscriptionId]) ON DELETE CASCADE,
       CONSTRAINT [CK_SubscriptionDeliveryDays_Range] CHECK ([DayOfWeek] BETWEEN 1 AND 7)
   );
   ```
   - *Advantages:*
     - Physically prevents duplicate weekday entries via the composite primary key.
     - Natural, indexable B-tree seek in daily run-sheet queries:
       `JOIN CustomerSubscriptionDeliveryDays d ON d.SubscriptionId = s.SubscriptionId AND d.DayOfWeek = @TargetDayOfWeek`.
     - Zero string parsing or string manipulation in T-SQL.

2. **Option 2: Canonical Comma-Separated String with Strict Database Constraints**
   - If keeping `DeliveryDays VARCHAR(20)` directly on `CustomerSubscriptions` to avoid multi-table joins:
   - Must reject duplicates (`1,1`), invalid day numbers (`0, 8, 9`), empty strings, leading/trailing commas, and double commas.
   - Canonical format: Digits must be strictly ascending with commas (`1,2,3,4,5,6,7`).

#### B. Adopted Architecture: Dual-Layer Defense
To achieve optimal query speed while ensuring absolute database integrity:
1. **Database Layer (Kevin's Implementation):**
   - Create normalized child table `dbo.CustomerSubscriptionDeliveryDays` (`SubscriptionId`, `DayOfWeek` PK).
   - In `CustomerSubscriptions`, store `DeliverySchedule` (`'Daily'`, `'Weekdays'`, `'Weekends'`, `'CustomDays'`) and canonical `DeliveryDays` (`VARCHAR(20)`).
   - Enforce consistency CHECK constraint on `CustomerSubscriptions`:
     ```sql
     CONSTRAINT [CK_CustomerSubscriptions_Schedule_Consistency] CHECK (
         ([DeliverySchedule] = 'Daily' AND [DeliveryDays] = '1,2,3,4,5,6,7') OR
         ([DeliverySchedule] = 'Weekdays' AND [DeliveryDays] = '1,2,3,4,5') OR
         ([DeliverySchedule] = 'Weekends' AND [DeliveryDays] = '6,7') OR
         ([DeliverySchedule] = 'CustomDays' AND [DeliveryDays] NOT IN ('1,2,3,4,5,6,7', '1,2,3,4,5', '6,7')
          AND [DeliveryDays] NOT LIKE '%[^1-7,]%' AND [DeliveryDays] NOT LIKE ',%' AND [DeliveryDays] NOT LIKE '%,' AND [DeliveryDays] NOT LIKE '%,,%')
     )
     ```
2. **Application Layer (Rachit's C# Validation):**
   - Validates that `DeliveryDays` parses into a sorted `HashSet<int>` containing distinct values in the range `1..7`.
   - Populates both `CustomerSubscriptions.DeliveryDays` and child rows in `CustomerSubscriptionDeliveryDays` atomically inside the save transaction.

---

## 6. Vacation Scope, Shift Matrix & Overlap Integrity

The `dbo.CustomerVacations` table governs customer non-delivery requests with approval gating.

### 6.1 Vacation Scope Over Categories and Shifts
1. **All-Category Invariant (Whole-Household Scope):**
   - In `CustomerVacations`, there is deliberately **no `CategoryId`** column.
   - When a customer is on vacation, the absence applies to **all milk categories and dairy products** subscribed by that customer for the specified shift.
   - *Rationale:* A vacation models family travel, illness, or absence from the home. Pausing a single product is a subscription adjustment, not a vacation.
2. **Shift Specificity:**
   - `Shift` column in `sql/03_CreateTables.sql` is verified as:
     `[Shift] NVARCHAR(20) NOT NULL DEFAULT ('Both') CHECK ([Shift] IN ('Morning', 'Evening', 'Both'))`.
   - `Both` is genuinely supported by the baseline schema!
   - If `Shift = 'Both'`, all deliveries for the entire day are locked out.
   - If `Shift = 'Morning'`, morning deliveries are locked out (`0.00` quantity), but evening deliveries proceed normally.
   - If `Shift = 'Evening'`, morning deliveries proceed normally, but evening deliveries are locked out.

### 6.2 Vacation Overlap Conflict Matrix (All 6 Combinations)

| Existing Active/Pending Shift | New Vacation Shift | Overlapping Date Window? | Conflict Outcome | Rule & Behavioral Rationale |
|---|---|---|---|---|
| **Both** | **Both** | Yes | **CONFLICT (Rejected)** | A whole-day vacation already covers both shifts. |
| **Both** | **Morning** | Yes | **CONFLICT (Rejected)** | Morning deliveries are already locked out by the existing `Both` vacation. |
| **Both** | **Evening** | Yes | **CONFLICT (Rejected)** | Evening deliveries are already locked out by the existing `Both` vacation. |
| **Morning** | **Both** | Yes | **CONFLICT (Rejected)** | A `Both` request conflicts with the active Morning vacation window. |
| **Evening** | **Both** | Yes | **CONFLICT (Rejected)** | A `Both` request conflicts with the active Evening vacation window. |
| **Morning** | **Morning** | Yes | **CONFLICT (Rejected)** | Two overlapping Morning vacations for the same customer cannot exist. |
| **Evening** | **Evening** | Yes | **CONFLICT (Rejected)** | Two overlapping Evening vacations for the same customer cannot exist. |
| **Morning** | **Evening** | Yes | **NO CONFLICT (Allowed)** | **Independent coexistence allowed!** Morning vacation exempts morning deliveries; Evening vacation exempts evening deliveries. |
| **Evening** | **Morning** | Yes | **NO CONFLICT (Allowed)** | **Independent coexistence allowed!** Symmetrical to above. |

### 6.3 Concurrency Protection Against Race Conditions
To prevent concurrent requests or approvals from bypassing checks and inserting overlapping records simultaneously:
- **Pessimistic Serialization:** The application begins a transaction and immediately acquires an exclusive update lock on the customer:
  ```sql
  BEGIN TRANSACTION;

  -- Serialize all vacation modifications for this specific customer
  SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId = @CustomerId;

  -- Atomic overlap evaluation:
  IF EXISTS (
      SELECT 1 FROM dbo.CustomerVacations WITH (UPDLOCK)
      WHERE CustomerId = @CustomerId
        AND Status IN ('Pending', 'Approved')
        AND VacationId != @ExcludeVacationId
        AND (Shift = 'Both' OR @Shift = 'Both' OR Shift = @Shift)
        AND (StartDate <= @EndDate AND EndDate >= @StartDate)
        AND (ResumedDate IS NULL OR @StartDate < ResumedDate)
  )
  BEGIN
      ROLLBACK TRANSACTION;
      THROW 50001, 'An active or pending vacation already covers this customer and shift.', 1;
  END

  INSERT INTO dbo.CustomerVacations (...);

  COMMIT TRANSACTION;
  ```

### 6.4 Early Resumption Workflow & Check Constraint
- Supported by `ResumedDate DATE NULL`.
- Lockout condition: `TargetDate >= StartDate AND TargetDate <= EndDate AND (ResumedDate IS NULL OR TargetDate < ResumedDate)`.
- When `ResumedDate` is set, normal deliveries and billing resume starting on that date. Past dates between `StartDate` and `ResumedDate - 1` remain recognized as approved vacation exemptions.
- **Database CHECK Constraint (Kevin to Implement):**
  ```sql
  CONSTRAINT [CK_CustomerVacations_ResumedDate] CHECK (
      [ResumedDate] IS NULL OR ([ResumedDate] >= [StartDate] AND [ResumedDate] <= [EndDate])
  );
  ```

---

## 7. Delivery Boy Assignment History & Concurrency Integrity

### 7.1 Single Active Agent Rule & Database Enforcement
- At any point in time, a customer has **at most one** active delivery boy assignment, or remains unassigned.
- **Database Enforcement:** Filtered unique index in `sql/04_CreateIndexes.sql`:
  ```sql
  CREATE UNIQUE NONCLUSTERED INDEX [UQ_ActiveCustomerAssignment]
  ON [dbo].[CustomerDeliveryBoyAssignments]([CustomerId])
  WHERE [IsActive] = 1 AND [AssignedTo] IS NULL;
  ```
  Guarantees that SQL Server will physically abort any transaction that attempts to create a second active assignment for the same customer.

### 7.2 Assignment Lifecycle, Correction vs. Reassignment, and Concurrency Integrity
An assignment record in `dbo.CustomerDeliveryBoyAssignments` models an effective-dated historical time interval `[AssignedFrom, AssignedTo]`. Because daily runsheet generation, driver mobile application routes, delivery entry logging, driver performance auditing, and delivery commission calculations link to these assignment intervals, preserving historical attribution is an uncompromisable system invariant.

#### 7.2.1 In-Place Administrative Correction vs. Forward Reassignment Rationale
- **The Core Risk:** An existing active assignment may have started days, weeks, or months earlier (`AssignedFrom < Today`). Changing `DeliveryBoyId` in place on that existing assignment row retroactively rewrites the assigned delivery boy for the **entire duration** of that assignment window! Even if no delivery has occurred yet today, an in-place update would retroactively strip credit and attribution from the previous delivery boy for all prior days and reassign them to the new delivery boy.
- **Strict In-Place Correction Invariant:**
  An in-place update of `DeliveryBoyId` on an existing assignment row is permitted **strictly and exclusively if all three conditions are satisfied**:
  1. The existing active assignment began on the correction date (`AssignedFrom == CorrectionDate`).
  2. No daily delivery has been recorded under the existing assignment on that date (`COUNT(DailyMilkDeliveries on Today) == 0`).
  3. No historical delivery or commission attribution is affected.
- If any of these three conditions is not met, an in-place update is **strictly forbidden**, and the system must execute a forward-dated reassignment or reject the request.

#### 7.2.2 Detailed Behavior for the Five Operational Scenarios

```
                                  [ Assignment Modification Request ]
                                                  |
                    +-----------------------------+-----------------------------+
                    |                                                           |
          Requested Date < Today?                                    Requested Date >= Today
                    |                                                           |
           [ CASE 5: REJECTED ]                                                 |
         (Backdating Prohibited)                         +----------------------+----------------------+
                                                         |                                             |
                                            Today's Delivery Already Logged?               No Delivery Recorded Today
                                                         |                                             |
                                                [ CASE 3: NEXT DAY ]                                   |
                                             (Starts Tomorrow: Today+1)               +----------------+----------------+
                                                                                      |                                 |
                                                                           AssignedFrom == Today?            AssignedFrom < Today?
                                                                                      |                                 |
                                                                             [ CASE 1: IN-PLACE ]             [ CASE 2: FORWARD ]
                                                                             (Clerical Correction)          (Old: D-1, New: Today)
```

##### Case 1: Assignment Started Today and No Delivery Recorded Today (`AssignedFrom == Today` AND No Deliveries on `Today`)
- **Operational Scenario:** The Admin assigned Delivery Boy A earlier today (e.g., during morning onboarding or route adjustment). Prior to any delivery runsheet execution or delivery record being logged on today's date, the Admin realizes a clerical mistake was made (e.g., selected Delivery Boy A instead of Delivery Boy B).
- **Attribution & Integrity Assessment:** Because `AssignedFrom` is today, no prior calendar days exist in this assignment window. Because no deliveries have been logged today, no runsheet lines or driver commissions exist.
- **Rule & System Action:** **In-Place Clerical Correction is Permitted.**
  - The existing active assignment row is updated in place.
  - Avoids creating redundant zero-day micro-interval rows.
  - T-SQL Implementation:
    ```sql
    UPDATE dbo.CustomerDeliveryBoyAssignments
    SET DeliveryBoyId = @NewDeliveryBoyId,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId,
        Notes = ISNULL(Notes + ' | ', '') + 'Admin clerical correction on start date'
    WHERE AssignmentId = @ActiveAssignmentId;
    ```
  - Master Record Update: `Customers.CurrentDeliveryBoyId = @NewDeliveryBoyId`.
  - Audit Log: `REASSIGN_CLERICAL_CORRECTION_INPLACE`.

##### Case 2: Assignment Started Earlier and No Delivery Recorded Today (`AssignedFrom < Today` AND No Deliveries on `Today`)
- **Operational Scenario:** Delivery Boy A has been delivering to this customer for 14 days (`AssignedFrom = Today - 14`). Today is a delivery day, but morning/evening delivery has not yet been submitted. Admin wants to transfer the customer to Delivery Boy B starting today.
- **Attribution & Integrity Assessment:** If `DeliveryBoyId` were updated in place on the existing row, the previous 14 days of deliveries would be retroactively attributed to Delivery Boy B! That would corrupt historical runsheet archives, driver performance reports, and commission statements for Delivery Boy A.
- **Rule & System Action:** **In-Place Update is STRICTLY FORBIDDEN. Execute Standard Forward Reassignment Starting Today.**
  - Close the existing active assignment row on yesterday:
    ```sql
    UPDATE dbo.CustomerDeliveryBoyAssignments
    SET AssignedTo = DATEADD(day, -1, @CorrectionDate), -- Today - 1
        IsActive = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE AssignmentId = @ActiveAssignmentId;
    ```
  - Insert a brand new active assignment row starting today:
    ```sql
    INSERT INTO dbo.CustomerDeliveryBoyAssignments
        (CustomerId, DeliveryBoyId, AssignedFrom, AssignedTo, IsActive, Notes, CreatedAt, CreatedBy)
    VALUES
        (@CustomerId, @NewDeliveryBoyId, @CorrectionDate, NULL, 1, @Notes, SYSUTCDATETIME(), @AdminUserId);
    ```
  - Master Record Update: `Customers.CurrentDeliveryBoyId = @NewDeliveryBoyId`.
  - SQL Check Constraint `CK_CustomerDeliveryBoyAssignments_Dates` (`AssignedTo >= AssignedFrom`) is strictly satisfied because `AssignedFrom <= Today - 1`.
  - Prior days `[AssignedFrom, Today - 1]` remain permanently and immutably attributed to Delivery Boy A.
  - Audit Log: `REASSIGN_DELIVERY_BOY_FORWARD`.

##### Case 3: Today's Delivery Has Already Been Submitted Under Current Delivery Boy
- **Operational Scenario:** Delivery Boy A completed today's route and submitted a delivery record in `DailyMilkDeliveries` for this customer (e.g., 1.0L delivered at 6:45 AM). In the afternoon, Admin requests a reassignment to Delivery Boy B.
- **Attribution & Integrity Assessment:** Today's delivery and commission were already physically earned by Delivery Boy A and recorded in `DailyMilkDeliveries`. Reassigning effective today would create an irreconcilable conflict: two delivery boys claiming service on the same calendar date, or a delivery boy delivering milk on a date where another driver was officially assigned.
- **Rule & System Action:** **Same-Day Reassignment is BLOCKED. Reassignment Must Take Effect Tomorrow (`Today + 1`).**
  - The system detects: `EXISTS (SELECT 1 FROM dbo.DailyMilkDeliveries WHERE CustomerId = @CustomerId AND DeliveryDate = CAST(GETDATE() AS DATE))`.
  - Same-day effective transfer is rejected.
  - UI displays an explicit alert modal to Admin:
    > **⚠️ Delivery Already Completed for Today**
    > "A delivery has already been recorded for this customer today under [Current Delivery Boy]. Reassignment cannot be applied to today. The new assignment will take effect tomorrow ([Today + 1])."
  - Upon Admin confirmation, the system schedules the forward reassignment for tomorrow:
    - Close current assignment on today: `AssignedTo = CAST(GETDATE() AS DATE)`, `IsActive = 0`.
    - Insert new assignment starting tomorrow: `AssignedFrom = DATEADD(day, 1, CAST(GETDATE() AS DATE))`, `AssignedTo = NULL`, `IsActive = 1`.
    - `Customers.CurrentDeliveryBoyId` updates on `Today + 1`.
  - Audit Log: `REASSIGN_DELIVERY_BOY_NEXT_DAY`.

##### Case 4: Historical Deliveries Exist
- **Operational Scenario:** Customer has months of past deliveries and published monthly bills across multiple past delivery boy assignments.
- **Attribution & Integrity Assessment:** Historical operational records are legal accounting documents and must be completely immutable.
- **Rule & System Action:** **Strict Immutability of Historical Delivery Records.**
  - `DailyMilkDeliveries` records snapshot `DeliveryBoyId` at the time delivery was recorded.
  - Closed assignments (`AssignedTo < Today`, `IsActive = 0`) are read-only historical records.
  - Delivery boy commission reports and route audits join to historical delivery records and snapshotted assignment intervals.
  - Any customer reassignment affects only the current/future interval. It **never** cascades, recalculates, modifies, or deletes past delivery records or past monthly bills.

##### Case 5: Correction Requested for Past Dates (`@EffectiveDate < Today`)
- **Operational Scenario:** Admin attempts to backdate an assignment transfer (e.g., selecting an effective date of 3 days ago).
- **Attribution & Integrity Assessment:** Retroactively rewriting past assignment chains corrupts closed daily delivery runsheets, distorts driver route completion metrics, and invalidates calculated commission ledgers.
- **Rule & System Action:** **STRICTLY REJECTED.**
  - The API and Repository layers enforce:
    ```sql
    IF (@EffectiveDate < CAST(GETDATE() AS DATE))
    BEGIN
        RAISERROR('Backdated delivery boy reassignment is strictly prohibited. Historical delivery runsheets and commission statements are immutable.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
    ```
  - In the UI, the transfer effective date picker sets `min = Today`.

---

## 8. Customer Deactivation and Reactivation Date Boundaries

### 8.1 Standardized Deactivation Rule & Boundary Semantics
Across the Dairy Management System, the customer lifecycle is standardized upon a single, unambiguous date boundary rule:

> [!IMPORTANT]
> **Standardized Deactivation Rule:**
> **Deactivation is effective from the selected calendar date $D$. No new delivery or milk charge is permitted on or after $D$.**

#### Explicit Boundary Rules

| Boundary Dimension | Standard Operational Rule | Detail & Rationale |
|---|---|---|
| **A. Last Eligible Delivery Date** | **$D - 1$** (Calendar day prior to $D$) | The customer is eligible for daily milk deliveries up to and including $D - 1$. Starting on calendar date $D$, the customer is completely excluded from active delivery runsheets, mobile route lists, and automated daily charge generation. |
| **B. Termination of Active Records** | **$D - 1$** (End date set to $D - 1$) | In all standard cases where records started before $D$ (`AssignedFrom < D`, `EffectiveFrom < D`):<br>• Active Delivery Boy Assignment: `AssignedTo = D - 1`, `IsActive = 0`.<br>• Active Subscriptions: `EffectiveTo = D - 1`, `IsActive = 0`.<br>• Active Custom Pricing: `EffectiveTo = D - 1`, `IsActive = 0`.<br>• Future Vacations (`StartDate >= D`): `Status = 'Cancelled'`. |
| **C. Delivery Already Recorded on Date $D$** | **Explicit Admin Confirmation Modal; Service Ends $D + 1$** | If a delivery was already physically performed and recorded on date $D$ before deactivation was initiated:<br>• The system does **not** silently reject or void the recorded milk delivery.<br>• UI prompts Admin with a confirmation modal informing them that today's delivery will be billed and service will end effective tomorrow ($D + 1$).<br>• Upon confirmation, today's delivery is billed, delivery boy commission is preserved, and records close on date $D$ (`AssignedTo = D`, `EffectiveTo = D`). Deliveries cease on $D + 1$. |
| **D. Same-Day Onboarding / Start on Date $D$** | **Audited Same-Day Cancellation / Void Protocol** | If an assignment or subscription started on date $D$ (`AssignedFrom == D` or `EffectiveFrom == D`), setting end date to $D - 1$ would cause $D - 1 < D$, violating SQL CHECK constraints (`AssignedTo >= AssignedFrom`, `EffectiveTo >= EffectiveFrom`).<br>• If **no deliveries** occurred on $D$: Set `AssignedTo = D`, `EffectiveTo = D`, and `IsActive = 0` with audit notes *"Cancelled on onboarding date prior to delivery"*. Satisfies $D \ge D$ while `IsActive = 0` prevents runsheet inclusion.<br>• If **deliveries occurred** on $D$: Handled via Case C above (billed on $D$, stops $D + 1$). |
| **E. Reactivation Date Semantics** | **Effective from Resumption Date $R$ ($R \ge \text{Today}$); Creates NEW Records** | • Service resumes on date $R$. First eligible delivery date is $R$. Past dates ($R < \text{Today}$) are strictly rejected.<br>• Historical closed subscriptions and past assignments remain permanently closed and immutable.<br>• Reactivation inserts **NEW** rows with new surrogate primary keys (`SubscriptionId`, `AssignmentId`) starting on $R$, guaranteeing unbroken referential integrity for past invoices and delivery logs. |

---

### 8.2 Database Concurrency & Complete Transaction Specifications

#### 1. Standard Customer Deactivation Transaction (No Deliveries on $D$, `AssignedFrom < D`)
```sql
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Deactivate
    @CustomerId INT,
    @DeactivationDate DATE,
    @Reason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. Concurrency Lock on Customer Row
    SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId = @CustomerId;

    -- 2. Idempotency Check: Exit cleanly if already inactive
    IF EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = @CustomerId AND Status = 'Inactive' AND IsActive = 0)
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    -- 3. Verify No Delivery Exists on or after DeactivationDate
    IF EXISTS (SELECT 1 FROM dbo.DailyMilkDeliveries WHERE CustomerId = @CustomerId AND DeliveryDate >= @DeactivationDate AND Status = 'Delivered')
    BEGIN
        RAISERROR('Cannot execute standard deactivation: Deliveries already exist on or after deactivation date. Use confirmed deactivation workflow.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- 4. Update Customer Master Status and Soft Delete
    UPDATE dbo.Customers
    SET Status = 'Inactive',
        IsActive = 0,
        CurrentDeliveryBoyId = NULL,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId;

    -- 5. Close Active Delivery Boy Assignment (Safe Boundary Clamping)
    UPDATE dbo.CustomerDeliveryBoyAssignments
    SET AssignedTo = CASE
            WHEN AssignedFrom < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE AssignedFrom -- Satisfies AssignedTo >= AssignedFrom check constraint
        END,
        IsActive = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId,
        Notes = CASE
            WHEN AssignedFrom >= @DeactivationDate THEN ISNULL(Notes + ' | ', '') + 'Cancelled on start date (same-day deactivation)'
            ELSE Notes
        END
    WHERE CustomerId = @CustomerId AND AssignedTo IS NULL;

    -- 6. Close Active Subscriptions (Safe Boundary Clamping)
    UPDATE dbo.CustomerSubscriptions
    SET EffectiveTo = CASE
            WHEN EffectiveFrom < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE EffectiveFrom -- Satisfies EffectiveTo >= EffectiveFrom check constraint
        END,
        IsActive = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId AND (EffectiveTo IS NULL OR EffectiveTo >= @DeactivationDate);

    -- 7. Close Active Custom Pricing Overrides (Safe Boundary Clamping)
    UPDATE dbo.CustomerCustomPricing
    SET EffectiveTo = CASE
            WHEN EffectiveFrom < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE EffectiveFrom
        END,
        IsActive = 0
    WHERE CustomerId = @CustomerId AND (EffectiveTo IS NULL OR EffectiveTo >= @DeactivationDate);

    -- 8. Cancel Future Scheduled Vacations
    UPDATE dbo.CustomerVacations
    SET Status = 'Cancelled',
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId AND Status IN ('Pending', 'Approved') AND StartDate >= @DeactivationDate;

    -- 9. Record Audit Log
    INSERT INTO dbo.AuditLogs (EntityName, EntityId, Action, NewValues, UserId, TimestampUtc)
    VALUES ('Customers', @CustomerId, 'DEACTIVATE_CUSTOMER',
            JSON_OBJECT('DeactivationDate': @DeactivationDate, 'Reason': @Reason),
            @AdminUserId, SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
```

#### 2. Confirmed Deactivation When Delivery Already Recorded on Date $D$
When a delivery has already been logged on date $D$ and Admin explicitly confirms via modal:
```sql
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Deactivate_WithRecordedDelivery
    @CustomerId INT,
    @DeactivationDate DATE, -- The date on which delivery was already recorded
    @Reason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. Lock Customer Row
    SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId = @CustomerId;

    -- 2. Master Record Soft Delete
    UPDATE dbo.Customers
    SET Status = 'Inactive',
        IsActive = 0,
        CurrentDeliveryBoyId = NULL,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId;

    -- 3. Close Active Assignment ending ON DeactivationDate D (Preserves Today's Commission & Run)
    UPDATE dbo.CustomerDeliveryBoyAssignments
    SET AssignedTo = @DeactivationDate,
        IsActive = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId,
        Notes = ISNULL(Notes + ' | ', '') + 'Closed on delivery date; delivery billed; service terminated'
    WHERE CustomerId = @CustomerId AND AssignedTo IS NULL;

    -- 4. Close Active Subscriptions ending ON DeactivationDate D (Billed through Date D)
    UPDATE dbo.CustomerSubscriptions
    SET EffectiveTo = @DeactivationDate,
        IsActive = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId AND (EffectiveTo IS NULL OR EffectiveTo >= @DeactivationDate);

    -- 5. Close Custom Pricing ending ON DeactivationDate D
    UPDATE dbo.CustomerCustomPricing
    SET EffectiveTo = @DeactivationDate,
        IsActive = 0
    WHERE CustomerId = @CustomerId AND (EffectiveTo IS NULL OR EffectiveTo >= @DeactivationDate);

    -- 6. Cancel Future Scheduled Vacations starting after Date D
    UPDATE dbo.CustomerVacations
    SET Status = 'Cancelled',
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId AND Status IN ('Pending', 'Approved') AND StartDate > @DeactivationDate;

    -- 7. Audit Log with Explicit Preserved Delivery Flag
    INSERT INTO dbo.AuditLogs (EntityName, EntityId, Action, NewValues, UserId, TimestampUtc)
    VALUES ('Customers', @CustomerId, 'DEACTIVATE_CONFIRMED_WITH_DELIVERY',
            JSON_OBJECT('DeactivationDate': @DeactivationDate, 'BilledDeliveryPreserved': 1, 'Reason': @Reason),
            @AdminUserId, SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
```

#### 3. Audited Same-Day Onboarding Void Protocol (`AssignedFrom == D`, No Deliveries)
When customer was onboarded today on date $D$ with `AssignedFrom = D` and deactivated today before any delivery occurred:
```sql
-- Clamping AssignedTo and EffectiveTo to AssignedFrom/EffectiveFrom satisfies
-- CK_CustomerDeliveryBoyAssignments_Dates (AssignedTo >= AssignedFrom: D >= D)
-- and CK_CustomerSubscriptions_Dates (EffectiveTo >= EffectiveFrom: D >= D).
-- Setting IsActive = 0 completely excludes row from runsheets.
UPDATE dbo.CustomerDeliveryBoyAssignments
SET AssignedTo = AssignedFrom,
    IsActive = 0,
    Notes = ISNULL(Notes + ' | ', '') + 'Audited void on onboarding date prior to delivery',
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @AdminUserId
WHERE CustomerId = @CustomerId AND AssignedFrom = @DeactivationDate AND AssignedTo IS NULL;

UPDATE dbo.CustomerSubscriptions
SET EffectiveTo = EffectiveFrom,
    IsActive = 0,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @AdminUserId
WHERE CustomerId = @CustomerId AND EffectiveFrom = @DeactivationDate AND (EffectiveTo IS NULL OR EffectiveTo >= @DeactivationDate);
```

#### 4. Reactivation Transaction Specification (Resumption on Date $R$, Creating NEW Records)
```sql
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Reactivate
    @CustomerId INT,
    @ReactivationDate DATE, -- Must be >= CAST(GETDATE() AS DATE)
    @RouteId INT,
    @DeliverySequence INT,
    @DeliveryBoyId INT,
    @Notes NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@ReactivationDate < CAST(GETDATE() AS DATE))
    BEGIN
        RAISERROR('Reactivation date cannot be in the past.', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;

    -- 1. Lock Customer Row
    SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId = @CustomerId;

    -- 2. Verify Customer is Currently Inactive
    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = @CustomerId AND Status = 'Inactive')
    BEGIN
        RAISERROR('Customer is not in Inactive status.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- 3. Update Customer Master Record
    UPDATE dbo.Customers
    SET Status = 'Active',
        IsActive = 1,
        RouteId = @RouteId,
        DeliverySequence = @DeliverySequence,
        CurrentDeliveryBoyId = @DeliveryBoyId,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @AdminUserId
    WHERE CustomerId = @CustomerId;

    -- 4. Insert BRAND NEW Delivery Boy Assignment Record (Past Assignments Remain Immutable)
    INSERT INTO dbo.CustomerDeliveryBoyAssignments
        (CustomerId, DeliveryBoyId, AssignedFrom, AssignedTo, IsActive, Notes, CreatedAt, CreatedBy)
    VALUES
        (@CustomerId, @DeliveryBoyId, @ReactivationDate, NULL, 1, @Notes, SYSUTCDATETIME(), @AdminUserId);

    -- 5. Subscriptions: Caller inserts brand new rows into dbo.CustomerSubscriptions
    --    (with EffectiveFrom = @ReactivationDate, EffectiveTo = NULL, IsActive = 1)
    --    and into child table dbo.CustomerSubscriptionDeliveryDays.
    --    Past closed subscriptions remain permanently closed with their historical EffectiveTo dates.

    -- 6. Audit Log
    INSERT INTO dbo.AuditLogs (EntityName, EntityId, Action, NewValues, UserId, TimestampUtc)
    VALUES ('Customers', @CustomerId, 'REACTIVATE_CUSTOMER',
            JSON_OBJECT('ReactivationDate': @ReactivationDate, 'RouteId': @RouteId, 'DeliveryBoyId': @DeliveryBoyId),
            @AdminUserId, SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
```

---

### 8.3 Safe Handling of Special Deactivation Cases
1. **Customer Has No Active Delivery Boy (`CurrentDeliveryBoyId IS NULL`):** Procedure step matches 0 rows and completes safely.
2. **Customer Has No Active Subscription:** Procedure step matches 0 rows and completes safely.
3. **Historical Invoices & Open Items:** `DailyMilkDeliveries`, `MonthlyBills`, and `Payments` are untouched. Unpaid balances remain on their respective monthly bills (`BalanceAmount`) and appear on the Aged Debtors ledger.

---

## 9. Billing and Historical Data Protection

### 9.1 Open-Item Ledger Protection
- Customers accumulate monthly bills generated by calendar month (e.g., `BillMonth = 11`, `BillYear = 2026`).
- Each bill contains line items snapshotting total litres and amounts per category.
- **Published Bill Protection:** Once an Admin publishes a monthly bill (`Status = 'Published'`), the bill is locked.
- Editing a customer's address, name, route, or category subscription must **never** alter existing bills.
- If a customer is deactivated with an unpaid balance, their account status becomes `Inactive`, but their outstanding balance in `MonthlyBills.BalanceAmount` remains visible in the Aged Debtors / Overdue ledger.

### 9.2 Audit Log Traceability
All sensitive customer modifications (rate overrides, deactivations, reactivations, delivery boy reassignments) must record an entry in `dbo.AuditLogs`:
- `EntityName = 'Customers'`
- `EntityId = CustomerId`
- `Action = 'UPDATE'`, `'DEACTIVATE'`, `'REASSIGN'`, `'CUSTOM_PRICE'`
- `OldValues` and `NewValues` serialized as JSON strings.

---

## 10. Practical Validation Rules

| Domain Field | Required? | Type / Range | Validation Rule & Error Message |
|---|---|---|---|
| `FullName` | **Required** | `NVARCHAR(100)` | 2–100 chars; letters, spaces, dots, hyphens. *"Customer name must be between 2 and 100 characters."* |
| `FullNameGujarati` | Optional | `NVARCHAR(100)` | Max 100 chars; Unicode Gujarati characters. |
| `Phone` | **Required** | `VARCHAR(15)` | 10–15 digits, optional leading `+`. Regex: `^\+?[0-9]{10,15}$`. *"Enter a valid 10 to 15 digit mobile number."* |
| `AlternatePhone` | Optional | `VARCHAR(15)` | If provided, 10–15 digits. Cannot equal primary `Phone`. |
| `Address` | **Required** | `NVARCHAR(250)` | 5–250 chars. *"Address is required and must be between 5 and 250 characters."* |
| `Area` | Optional | `NVARCHAR(100)` | Max 100 chars. |
| `Landmark` | Optional | `NVARCHAR(100)` | Max 100 chars. |
| `RouteId` | **Required** | `INT` | Must be `> 0` and exist in `Routes`. *"Please select a valid delivery route."* |
| `DeliverySequence` | **Required** | `INT` | Range: `0` to `9999`. *"Sequence number must be non-negative."* |
| `PreferredShift` | **Required** | `NVARCHAR(20)` | Must be `'Morning'`, `'Evening'`, or `'Both'`. |
| `DeliveryStartDate` | **Required** | `DATE` | Must be a valid date. Cannot be more than 30 days in the past. |
| `CategoryId` | **Required** | `INT` | Must exist and be `IsActive = 1` in `MilkCategories`. |
| `PackagingQuantity`| **Required** | `DECIMAL(18,3)` | Range: `0.250` to `50.000` units. *"Quantity must be between 0.25 and 50."* |
| `CustomPrice` | Optional | `DECIMAL(18,2)` | If specified, must be `> 0.00` and `<= 500.00`. *"Custom price must be positive."* |
| `Vacation StartDate`| **Required** | `DATE` | Cannot be earlier than current date minus 2 days (grace period). |
| `Vacation EndDate` | **Required** | `DATE` | Must be `>= StartDate`. Maximum window: 90 days. |

---

## 11. Customer Management Operations (16 Operations)

| # | Operation Name | Actor | Method | Auth Required | Confirmation / Modal? | Audit Logged? |
|---|---|---|---|---|---|---|
| **1** | Create Customer | Admin | `POST /Customer/Create` | Admin Only | No | Yes |
| **2** | View Customer Profile | Admin, DB (Assigned) | `GET /Customer/Details/{id}` | Admin / DB (Self) | No | No |
| **3** | Edit Customer Details | Admin | `POST /Customer/Edit/{id}` | Admin Only | No | Yes |
| **4** | Search Customers | Admin, Delivery Boy | `GET /Customer/Search` | Admin / DB | No | No |
| **5** | Filter Customers | Admin | `GET /Customer/Index` | Admin Only | No | No |
| **6** | Configure Milk Categories | Admin | `POST /Customer/Subscriptions/Save` | Admin Only | Yes | Yes |
| **7** | Configure Custom Price | Admin | `POST /Customer/Pricing/Save` | Admin Only | Yes | Yes |
| **8** | Configure Delivery Schedule | Admin | `POST /Customer/Schedule/Save` | Admin Only | No | Yes |
| **9** | Assign Delivery Boy | Admin | `POST /Customer/AssignDeliveryBoy` | Admin Only | No | Yes |
| **10** | Reassign Delivery Boy | Admin | `POST /Customer/ReassignDeliveryBoy` | Admin Only | Yes (Impact warning) | Yes |
| **11** | Unassign Delivery Boy | Admin | `POST /Customer/UnassignDeliveryBoy` | Admin Only | Yes (Confirmation) | Yes |
| **12** | Register Vacation | Admin, DB (Report) | `POST /Customer/Vacation/Create` | Admin / DB | No | Yes |
| **13** | Resume Early Vacation | Admin | `POST /Customer/Vacation/ResumeEarly`| Admin Only | Yes | Yes |
| **14** | Deactivate Customer | Admin | `POST /Customer/Deactivate/{id}` | Admin Only | Yes (Strict modal) | Yes |
| **15** | Reactivate Customer | Admin | `POST /Customer/Reactivate/{id}` | Admin Only | Yes (Modal + Setup) | Yes |
| **16** | View Customer History | Admin | `GET /Customer/History/{id}` | Admin Only | No | No |

---

## 12. Authorization Matrix: Implemented vs. Designed Specifications

### 12.1 Security Behavior Actually Implemented and Verified (Day 2 Foundation)
The following authorization mechanisms have been implemented in C# and verified through 21 passing automated tests:
1. **Cookie Authentication Pipeline:** `Program.cs` configures `CookieAuthenticationDefaults` with role-aware path redirection via `RouteClassifier`.
2. **Dashboard Role Enforcement:**
   - `HomeController.Index` enforces `[Authorize(Roles = "Admin")]`.
   - `HomeController.Privacy` enforces `[Authorize(Roles = "DeliveryBoy")]`.
3. **Cross-Role Transition Protection:** `AuthController.SwitchRole` enforces `[Authorize]`, `[HttpPost]`, and `[ValidateAntiForgeryToken]`.
4. **Signout Security:** `AuthController.Logout` enforces `[Authorize]`, `[HttpPost]`, and `[ValidateAntiForgeryToken]`.
5. **Open Redirect Defense:** `AuthController.RedirectToLocal` strictly validates local return destinations via `Url.IsLocalUrl`.

### 12.2 Security Architecture Designed for Phase 3 (Customer Management Specification)
> [!IMPORTANT]
> The following controller authorization rules represent the **approved design contract** for Phase 3 implementation. The controllers and views will be implemented after Kevin completes the database migration.

| Proposed Controller Action | HTTP Verb | Authorization Decorator | Access Scope |
|---|---|---|---|
| `CustomerController.Index` | `GET` | `[Authorize(Roles = "Admin")]` | System-wide customer directory across all routes. |
| `CustomerController.Details` | `GET` | `[Authorize(Roles = "Admin,DeliveryBoy")]` | Admin: Full profile. Delivery Boy: View only if `CurrentDeliveryBoyId == CurrentUser.UserId`. |
| `CustomerController.Create` | `GET`, `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only onboarding. |
| `CustomerController.Edit` | `GET`, `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only profile modifications. |
| `CustomerController.Subscriptions` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only multi-category configuration. |
| `CustomerController.Pricing` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only rate card overrides (hidden from Delivery Boy). |
| `CustomerController.Reassign` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only route and agent reassignments. |
| `CustomerController.Deactivate` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only customer termination. |
| `CustomerController.Reactivate` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only customer resumption. |
| `CustomerVacationController.Create` | `POST` | `[Authorize(Roles = "Admin,DeliveryBoy")]`, `[ValidateAntiForgeryToken]` | Admin creates `Approved`. Delivery Boy submits `Pending` for assigned customer only. |
| `CustomerVacationController.Approve` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only approval gate. |
| `CustomerVacationController.Reject` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only rejection. |
| `CustomerVacationController.ResumeEarly` | `POST` | `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` | Admin only early return workflow. |

---

## 13. Audit Requirements

Sensitive modifications require structured records in `dbo.AuditLogs`:
```json
{
  "Action": "CUSTOMER_REASSIGNED",
  "CustomerId": 104,
  "CustomerCode": "CUST-00104",
  "PreviousDeliveryBoyId": 4,
  "NewDeliveryBoyId": 7,
  "AssignedFrom": "2026-12-01",
  "Reason": "Route 4 territorial balancing",
  "ModifiedByUserId": 1004,
  "TimestampUtc": "2026-11-17T18:30:00Z"
}
```

---

## 14. Existing Database Gap Analysis

Comparison between Day 3 requirements and Kevin's approved database schema (`sql/03_CreateTables.sql` & `docs/database/database-design.md`):

| Requirement | Existing Table / Column | Supported? | Gap Identified | Architectural Recommendation |
|---|---|---|---|---|
| **Unique Customer Code** | `Customers.CustomerCode` | **YES** | Format: `CUST-XXXXX` | Fully supported (`VARCHAR(20) NOT NULL UNIQUE`). |
| **English Customer Name** | `Customers.FullName` | **YES** | None | Fully supported (`NVARCHAR(100) NOT NULL`). |
| **Gujarati Customer Name** | `Customers` table | **GAP** | `Customers` lacks dedicated Gujarati name column. | **Add `FullNameGujarati NVARCHAR(100) NULL`** to `Customers`. |
| **Area / Locality** | `Customers.Address`, `Landmark` | **PARTIAL** | Locality currently concatenated into `Address`. | **Add `Area NVARCHAR(100) NULL`** to `Customers`. |
| **Customer Notes** | `Customers` table | **GAP** | Lacks general delivery instructions column. | **Add `Notes NVARCHAR(500) NULL`** to `Customers`. |
| **Delivery Start Date** | `Customers.CreatedAt` | **PARTIAL** | `CreatedAt` is audit timestamp; business start date implicit. | **Add `DeliveryStartDate DATE NULL`** to `Customers`. |
| **Delivery Schedule (Weekdays)** | `CustomerSubscriptions` | **GAP** | Lacks day-of-week recurrence and child mapping. | **Add `DeliverySchedule`, `DeliveryDays`** and child table `dbo.CustomerSubscriptionDeliveryDays`. |
| **Delivery Boy Assignment History** | `CustomerDeliveryBoyAssignments` | **YES** | None | Fully supported (`AssignedFrom`, `AssignedTo`, filtered unique index). |
| **Multi-Category Subscriptions** | `CustomerSubscriptions` | **YES** | None | Fully supported (`CustomerId`, `CategoryId`, `Shift`, `PackagingQuantity`). |
| **Custom Unit Pricing Overrides** | `CustomerCustomPricing` | **YES** | None | Fully supported (`CustomerId`, `CategoryId`, `CustomPrice`, `EffectiveFrom`, `EffectiveTo`). |
| **Vacation Approval & Lockout** | `CustomerVacations` | **YES** | None | Fully supported (`StartDate`, `EndDate`, `ResumedDate`, `Status`, `ApprovedBy`). |
| **Soft Deletion & Active Flag** | `Customers.IsActive`, `Status` | **YES** | None | Fully supported (`Status IN ('Active', 'Suspended', 'Inactive')`, `IsActive BIT`). |

---

## 15. Proposed Schema Changes (For Kevin's Implementation)

> [!IMPORTANT]
> In accordance with project policy, Rachit will **NOT** modify Kevin's approved SQL scripts (`sql/*.sql`) or execute database migrations. The following proposed migration script is provided for Kevin's review and execution in Phase 3.

```sql
-- PROPOSED SCHEMA MIGRATION: Customer Management Enhancements
-- Target: DairyManagementDb
-- Author: Prepared by Rachit for Kevin's review and implementation

-- 1. Add FullNameGujarati to Customers
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'FullNameGujarati')
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [FullNameGujarati] NVARCHAR(100) NULL;
    PRINT 'Added column Customers.FullNameGujarati';
END
GO

-- 2. Add Area to Customers
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'Area')
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [Area] NVARCHAR(100) NULL;
    PRINT 'Added column Customers.Area';
END
GO

-- 3. Add Notes to Customers
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'Notes')
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [Notes] NVARCHAR(500) NULL;
    PRINT 'Added column Customers.Notes';
END
GO

-- 4. Add DeliveryStartDate to Customers
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'DeliveryStartDate')
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [DeliveryStartDate] DATE NULL;
    PRINT 'Added column Customers.DeliveryStartDate';
END
GO

-- 5. Add DeliverySchedule to CustomerSubscriptions
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]') AND name = N'DeliverySchedule')
BEGIN
    ALTER TABLE [dbo].[CustomerSubscriptions] ADD [DeliverySchedule] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerSubscriptions_DeliverySchedule] DEFAULT ('Daily');
    ALTER TABLE [dbo].[CustomerSubscriptions] ADD CONSTRAINT [CK_CustomerSubscriptions_DeliverySchedule] CHECK ([DeliverySchedule] IN ('Daily', 'Weekdays', 'Weekends', 'CustomDays'));
    PRINT 'Added column CustomerSubscriptions.DeliverySchedule with CHECK constraint';
END
GO

-- 6. Add DeliveryDays to CustomerSubscriptions with strict format CHECK constraint
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]') AND name = N'DeliveryDays')
BEGIN
    ALTER TABLE [dbo].[CustomerSubscriptions] ADD [DeliveryDays] VARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerSubscriptions_DeliveryDays] DEFAULT ('1,2,3,4,5,6,7');
    ALTER TABLE [dbo].[CustomerSubscriptions] ADD CONSTRAINT [CK_CustomerSubscriptions_DeliveryDays] CHECK (
        [DeliveryDays] NOT LIKE '%[^1-7,]%' AND
        [DeliveryDays] NOT LIKE ',%' AND
        [DeliveryDays] NOT LIKE '%,' AND
        [DeliveryDays] NOT LIKE '%,,%' AND
        LEN([DeliveryDays]) BETWEEN 1 AND 13
    );
    PRINT 'Added column CustomerSubscriptions.DeliveryDays with validation CHECK constraint';
END
GO

-- 7. Normalized Child Table for Subscription Delivery Days (Eliminates Duplicates & Enables Direct B-Tree Index Seeks)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerSubscriptionDeliveryDays')
BEGIN
    CREATE TABLE [dbo].[CustomerSubscriptionDeliveryDays] (
        [SubscriptionId] INT NOT NULL,
        [DayOfWeek] TINYINT NOT NULL, -- 1=Monday, 7=Sunday (ISO 8601)
        CONSTRAINT [PK_CustomerSubscriptionDeliveryDays] PRIMARY KEY CLUSTERED ([SubscriptionId], [DayOfWeek]),
        CONSTRAINT [FK_CustomerSubscriptionDeliveryDays_Subscription] FOREIGN KEY ([SubscriptionId])
            REFERENCES [dbo].[CustomerSubscriptions]([SubscriptionId]) ON DELETE CASCADE,
        CONSTRAINT [CK_CustomerSubscriptionDeliveryDays_Range] CHECK ([DayOfWeek] BETWEEN 1 AND 7)
    );
    PRINT 'Created normalized child table CustomerSubscriptionDeliveryDays';
END
GO

-- 8. Add ResumedDate validation check to CustomerVacations
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CustomerVacations_ResumedDate')
BEGIN
    ALTER TABLE [dbo].[CustomerVacations] ADD CONSTRAINT [CK_CustomerVacations_ResumedDate] CHECK (
        [ResumedDate] IS NULL OR ([ResumedDate] >= [StartDate] AND [ResumedDate] <= [EndDate])
    );
    PRINT 'Added constraint CK_CustomerVacations_ResumedDate';
END
GO
```

---

## 16. Backend Contracts & Interface Specifications

### 16.1 Concrete Domain Entities and ViewModels
Located in dedicated files in `Models/Customer/`:
- `Customer.cs` — Core customer entity.
- `CustomerListItemViewModel.cs` — High-performance read model for customer grid.
- `CustomerDetailsViewModel.cs` — Comprehensive profile view with subscriptions and assignment history.
- `CreateCustomerViewModel.cs` — Input model for customer onboarding.
- `EditCustomerViewModel.cs` — Dedicated input model for customer profile updates.
- `CustomerSubscriptionModel.cs` — Multi-category subscription lines.
- `CustomerCustomPriceModel.cs` — Dedicated custom price override item model.
- `CustomerFilterCriteria.cs` — Dedicated directory search criteria model.
- `CustomerOperationResult.cs` — Dedicated standardized result model.
- `CustomerReassignmentViewModel.cs` — Dedicated delivery boy reassignment modal model.
- `CustomerVacationViewModel.cs` — Dedicated vacation entry and approval view model.

### 16.2 Repository Interfaces (`Repositories/`)
- `ICustomerRepository.cs` — Customer master CRUD, code generation, phone uniqueness checks, and assignment history.
- `ICustomerSubscriptionRepository.cs` — Multi-category subscription lines and custom unit price overrides.
- `ICustomerVacationRepository.cs` — Vacation requests, approval gates, overlap checks, and early resumptions.

### 16.3 Service Interfaces (`Services/`)
- `ICustomerService.cs` — High-level customer management workflows, validation, code generation, assignment, deactivation, and reactivation.
- `ICustomerVacationService.cs` — Vacation request lifecycle, approval gate, and delivery runsheet lockout checks.

---

## 17. Edge Cases and Failure Scenarios

1. **Duplicate Mobile Numbers:**
   - Handled via `IsPhoneUniqueAsync` before database insert, returning user-friendly validation error: *"Mobile number already registered to another customer."*
2. **Vacation Across Month Boundaries:**
   - E.g., Nov 28 to Dec 04. The vacation lockout correctly evaluates dates across month boundaries. The billing engine applies zero charges for Nov 28–30 on the November bill and Dec 01–04 on the December bill.
3. **Mid-Month Delivery Boy Reassignment:**
   - E.g., Reassigned on Nov 16. Deliveries on Nov 01–15 retain previous Delivery Boy ID for commission calculation; deliveries on Nov 16–30 credit the new Delivery Boy.
4. **Reactivating Customer with Inactive Category:**
   - If a customer previously subscribed to a category that has since been deactivated (`MilkCategories.IsActive = 0`), reactivation blocks selecting that category and forces selection of currently active products.
5. **Simultaneous Vacation and Delivery Attempt:**
   - Delivery Boy app displays customer as locked. If a rogue request attempts to submit delivery for a locked customer, server-side validation in `DailyMilkEntryService` rejects the entry.

---

## 18. Acceptance Criteria

1. **Admin Control:** Only authenticated users with the `Admin` role can create, edit, reassign, deactivate, or reactivate customers.
2. **Delivery Boy Read-Only Scope:** Delivery Boys can only query customer names and addresses assigned to their own route; they cannot view pricing or financial ledgers.
3. **Multi-Category Subscriptions:** A customer can have independent quantities and prices across multiple milk categories.
4. **Immutable History:** Profile or price changes never alter past delivery entries or published bills.
5. **Zero Vacation Billing:** Customers on approved vacation receive zero delivery charges for vacation dates.
6. **No Cascading Deletions:** Master records cannot be physically deleted from the database.
7. **Clean Compilation:** Solution compiles with zero warnings and zero errors under .NET 9.
