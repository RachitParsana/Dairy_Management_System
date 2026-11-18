-- ====================================================================================
-- Script 06: Customer Management Database Enhancements
-- Project: Dairy Management System (DMS)
-- Database: DairyManagementDb
-- Phase: Day 3 Customer Management Implementation (Kevin - Database Engineer)
-- Authority: docs/customer-management/customer-lifecycle-and-backend-design.md v1.3.0
-- Description: Additive, safe migration applying customer master enhancements,
--              multi-category subscription delivery schedules, normalized
--              CustomerSubscriptionDeliveryDays child table, vacation overlap integrity,
--              concurrency-safe reassignments, and deactivation/reactivation procedures.
-- ====================================================================================

USE [DairyManagementDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting 06_CustomerManagementEnhancements migration...';
PRINT '====================================================================================';
GO

-- ====================================================================================
-- 1. Customers Table Enhancements
-- ====================================================================================

-- 1.1 FullNameGujarati (Unicode support for localized runsheets)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'FullNameGujarati'
)
BEGIN
    PRINT 'Adding column FullNameGujarati to Customers...';
    ALTER TABLE [dbo].[Customers]
    ADD [FullNameGujarati] NVARCHAR(100) NULL;
END
GO

-- 1.2 Area (Locality / neighborhood categorization)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'Area'
)
BEGIN
    PRINT 'Adding column Area to Customers...';
    ALTER TABLE [dbo].[Customers]
    ADD [Area] NVARCHAR(100) NULL;
END
GO

-- 1.3 Notes (Delivery instructions & profile notes)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'Notes'
)
BEGIN
    PRINT 'Adding column Notes to Customers...';
    ALTER TABLE [dbo].[Customers]
    ADD [Notes] NVARCHAR(500) NULL;
END
GO

-- 1.4 DeliveryStartDate (Official service start date)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = N'DeliveryStartDate'
)
BEGIN
    PRINT 'Adding column DeliveryStartDate to Customers...';
    ALTER TABLE [dbo].[Customers]
    ADD [DeliveryStartDate] DATE NULL;
END
GO


-- ====================================================================================
-- 2. CustomerSubscriptions Delivery Schedule & Canonical Days
-- ====================================================================================

-- 2.1 DeliverySchedule column ('Daily', 'Weekdays', 'Weekends', 'CustomDays')
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]') AND name = N'DeliverySchedule'
)
BEGIN
    PRINT 'Adding column DeliverySchedule to CustomerSubscriptions...';
    ALTER TABLE [dbo].[CustomerSubscriptions]
    ADD [DeliverySchedule] NVARCHAR(20) NOT NULL
        CONSTRAINT [DF_CustomerSubscriptions_DeliverySchedule] DEFAULT ('Daily');
END
GO

-- 2.2 DeliveryDays column (Canonical comma-separated weekdays: '1,2,3,4,5,6,7')
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]') AND name = N'DeliveryDays'
)
BEGIN
    PRINT 'Adding column DeliveryDays to CustomerSubscriptions...';
    ALTER TABLE [dbo].[CustomerSubscriptions]
    ADD [DeliveryDays] VARCHAR(20) NOT NULL
        CONSTRAINT [DF_CustomerSubscriptions_DeliveryDays] DEFAULT ('1,2,3,4,5,6,7');
END
GO

-- 2.3 CHECK Constraint: DeliverySchedule valid values
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_CustomerSubscriptions_Schedule' AND parent_object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]')
)
BEGIN
    PRINT 'Adding constraint CK_CustomerSubscriptions_Schedule...';
    ALTER TABLE [dbo].[CustomerSubscriptions]
    ADD CONSTRAINT [CK_CustomerSubscriptions_Schedule]
    CHECK ([DeliverySchedule] IN ('Daily', 'Weekdays', 'Weekends', 'CustomDays'));
END
GO

-- 2.4 CHECK Constraint: DeliverySchedule and DeliveryDays consistency validation
IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_CustomerSubscriptions_Schedule_Consistency' AND parent_object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]')
)
BEGIN
    ALTER TABLE [dbo].[CustomerSubscriptions] DROP CONSTRAINT [CK_CustomerSubscriptions_Schedule_Consistency];
END

PRINT 'Adding constraint CK_CustomerSubscriptions_Schedule_Consistency...';
ALTER TABLE [dbo].[CustomerSubscriptions]
ADD CONSTRAINT [CK_CustomerSubscriptions_Schedule_Consistency] CHECK (
    ([DeliverySchedule] = 'Daily' AND [DeliveryDays] = '1,2,3,4,5,6,7') OR
    ([DeliverySchedule] = 'Weekdays' AND [DeliveryDays] = '1,2,3,4,5') OR
    ([DeliverySchedule] = 'Weekends' AND [DeliveryDays] = '6,7') OR
    ([DeliverySchedule] = 'CustomDays' AND [DeliveryDays] NOT IN ('1,2,3,4,5,6,7', '1,2,3,4,5', '6,7')
     AND LEN([DeliveryDays]) > 0
     AND [DeliveryDays] NOT LIKE '%[^1-7,]%' AND [DeliveryDays] NOT LIKE ',%' AND [DeliveryDays] NOT LIKE '%,' AND [DeliveryDays] NOT LIKE '%,,%'
     AND [DeliveryDays] NOT LIKE '%1%1%'
     AND [DeliveryDays] NOT LIKE '%2%[1-2]%'
     AND [DeliveryDays] NOT LIKE '%3%[1-3]%'
     AND [DeliveryDays] NOT LIKE '%4%[1-4]%'
     AND [DeliveryDays] NOT LIKE '%5%[1-5]%'
     AND [DeliveryDays] NOT LIKE '%6%[1-6]%'
     AND [DeliveryDays] NOT LIKE '%7%[1-7]%')
);
GO


-- ====================================================================================
-- 3. Normalized Child Table: CustomerSubscriptionDeliveryDays & Consistency Triggers
-- ====================================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerSubscriptionDeliveryDays')
BEGIN
    PRINT 'Creating normalized child table CustomerSubscriptionDeliveryDays...';
    CREATE TABLE [dbo].[CustomerSubscriptionDeliveryDays] (
        [SubscriptionId] INT NOT NULL,
        [DayOfWeek] TINYINT NOT NULL, -- 1=Monday ... 7=Sunday (ISO 8601)
        CONSTRAINT [PK_CustomerSubscriptionDeliveryDays] PRIMARY KEY CLUSTERED ([SubscriptionId], [DayOfWeek]),
        CONSTRAINT [FK_SubscriptionDeliveryDays_Subscription] FOREIGN KEY ([SubscriptionId])
            REFERENCES [dbo].[CustomerSubscriptions]([SubscriptionId]) ON DELETE CASCADE,
        CONSTRAINT [CK_SubscriptionDeliveryDays_Range] CHECK ([DayOfWeek] BETWEEN 1 AND 7)
    );
END
GO

-- 3.1 trg_CustomerSubscriptionDeliveryDays_Validate: Enforces exact set equality on child INSERT/UPDATE
CREATE OR ALTER TRIGGER [dbo].[trg_CustomerSubscriptionDeliveryDays_Validate]
ON [dbo].[CustomerSubscriptionDeliveryDays]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Allow coordinated multi-table schedule transitions under usp_CustomerSubscription_UpdateSchedule
    IF CAST(SESSION_CONTEXT(N'SubscriptionScheduleTransition') AS INT) = 1 AND @@TRANCOUNT > 0
    BEGIN
        RETURN;
    END

    -- Verify exact set equality: normalized child rows must equal parent DeliveryDays
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT [SubscriptionId] FROM inserted) i
        INNER JOIN [dbo].[CustomerSubscriptions] s ON i.[SubscriptionId] = s.[SubscriptionId]
        CROSS APPLY (
            SELECT STRING_AGG(CAST(c.[DayOfWeek] AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY c.[DayOfWeek] ASC) AS ChildDays
            FROM [dbo].[CustomerSubscriptionDeliveryDays] c
            WHERE c.[SubscriptionId] = i.[SubscriptionId]
        ) ca
        WHERE ISNULL(ca.ChildDays, '') != s.[DeliveryDays]
    )
    BEGIN
        RAISERROR('Cannot insert or update CustomerSubscriptionDeliveryDays: Normalized child weekdays do not exactly match parent DeliveryDays schedule. Exact set equality is required.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

-- 3.2 trg_CustomerSubscriptionDeliveryDays_Delete: Enforces exact set equality on child DELETE
CREATE OR ALTER TRIGGER [dbo].[trg_CustomerSubscriptionDeliveryDays_Delete]
ON [dbo].[CustomerSubscriptionDeliveryDays]
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Allow coordinated multi-table schedule transitions under usp_CustomerSubscription_UpdateSchedule
    IF CAST(SESSION_CONTEXT(N'SubscriptionScheduleTransition') AS INT) = 1 AND @@TRANCOUNT > 0
    BEGIN
        RETURN;
    END

    -- If parent subscription still exists (not a cascade delete of parent)
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT [SubscriptionId] FROM deleted) d
        INNER JOIN [dbo].[CustomerSubscriptions] s ON d.[SubscriptionId] = s.[SubscriptionId]
        CROSS APPLY (
            SELECT STRING_AGG(CAST(c.[DayOfWeek] AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY c.[DayOfWeek] ASC) AS ChildDays
            FROM [dbo].[CustomerSubscriptionDeliveryDays] c
            WHERE c.[SubscriptionId] = d.[SubscriptionId]
        ) ca
        WHERE ISNULL(ca.ChildDays, '') != s.[DeliveryDays]
    )
    BEGIN
        RAISERROR('Cannot delete from CustomerSubscriptionDeliveryDays: Remaining child weekdays do not exactly match parent DeliveryDays schedule. Exact set equality is required.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

-- 3.3 trg_CustomerSubscriptions_ValidateDeliveryDaysUpdate: Enforces exact set equality on parent DeliveryDays UPDATE
CREATE OR ALTER TRIGGER [dbo].[trg_CustomerSubscriptions_ValidateDeliveryDaysUpdate]
ON [dbo].[CustomerSubscriptions]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Allow coordinated multi-table schedule transitions under usp_CustomerSubscription_UpdateSchedule
    IF CAST(SESSION_CONTEXT(N'SubscriptionScheduleTransition') AS INT) = 1 AND @@TRANCOUNT > 0
    BEGIN
        RETURN;
    END

    IF UPDATE([DeliverySchedule]) OR UPDATE([DeliveryDays])
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM inserted i
            CROSS APPLY (
                SELECT STRING_AGG(CAST(d.[DayOfWeek] AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY d.[DayOfWeek] ASC) AS ChildDays
                FROM [dbo].[CustomerSubscriptionDeliveryDays] d
                WHERE d.[SubscriptionId] = i.[SubscriptionId]
            ) ca
            WHERE ISNULL(ca.ChildDays, '') != i.[DeliveryDays]
        )
        BEGIN
            RAISERROR('Cannot update CustomerSubscriptions: Parent DeliveryDays does not exactly match normalized child rows in CustomerSubscriptionDeliveryDays. Exact set equality is required.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END
    END
END;
GO


-- ====================================================================================
-- 4. CustomerVacations Check Constraint
-- ====================================================================================

-- 4.1 CK_CustomerVacations_ResumedDate (ResumedDate must be between StartDate and EndDate)
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_CustomerVacations_ResumedDate' AND parent_object_id = OBJECT_ID(N'[dbo].[CustomerVacations]')
)
BEGIN
    PRINT 'Adding constraint CK_CustomerVacations_ResumedDate...';
    ALTER TABLE [dbo].[CustomerVacations]
    ADD CONSTRAINT [CK_CustomerVacations_ResumedDate]
    CHECK ([ResumedDate] IS NULL OR ([ResumedDate] >= [StartDate] AND [ResumedDate] <= [EndDate]));
END
GO


-- ====================================================================================
-- 5. Approved Performance & Integrity Indexes
-- ====================================================================================

-- 5.1 Customers FullName search index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_FullName' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_FullName...';
    CREATE NONCLUSTERED INDEX [IX_Customers_FullName]
    ON [dbo].[Customers]([FullName])
    INCLUDE ([CustomerCode], [Phone], [Area], [Status], [IsActive]);
END
GO

-- 5.2 Customers Phone search index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Phone' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_Phone...';
    CREATE NONCLUSTERED INDEX [IX_Customers_Phone]
    ON [dbo].[Customers]([Phone])
    INCLUDE ([FullName], [Status], [IsActive]);
END
GO

-- 5.3 Customers Area filter index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Area' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_Area...';
    CREATE NONCLUSTERED INDEX [IX_Customers_Area]
    ON [dbo].[Customers]([Area])
    WHERE [Area] IS NOT NULL;
END
GO

-- 5.4 Customers Status & IsActive grid filter index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Status_IsActive' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_Status_IsActive...';
    CREATE NONCLUSTERED INDEX [IX_Customers_Status_IsActive]
    ON [dbo].[Customers]([Status], [IsActive])
    INCLUDE ([CustomerId], [RouteId], [DeliverySequence], [CurrentDeliveryBoyId]);
END
GO

-- 5.5 Customers CurrentDeliveryBoyId mobile view index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_CurrentDeliveryBoyId' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_CurrentDeliveryBoyId...';
    CREATE NONCLUSTERED INDEX [IX_Customers_CurrentDeliveryBoyId]
    ON [dbo].[Customers]([CurrentDeliveryBoyId], [IsActive])
    INCLUDE ([CustomerId], [RouteId], [DeliverySequence]);
END
GO

-- 5.6 CustomerSubscriptionDeliveryDays DayOfWeek reverse lookup index for runsheet generation
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SubscriptionDeliveryDays_DayOfWeek' AND object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptionDeliveryDays]'))
BEGIN
    PRINT 'Creating performance index IX_SubscriptionDeliveryDays_DayOfWeek...';
    CREATE NONCLUSTERED INDEX [IX_SubscriptionDeliveryDays_DayOfWeek]
    ON [dbo].[CustomerSubscriptionDeliveryDays]([DayOfWeek])
    INCLUDE ([SubscriptionId]);
END
GO

-- 5.7 CustomerVacations Overlap Check performance index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerVacations_OverlapCheck' AND object_id = OBJECT_ID(N'[dbo].[CustomerVacations]'))
BEGIN
    PRINT 'Creating performance index IX_CustomerVacations_OverlapCheck...';
    CREATE NONCLUSTERED INDEX [IX_CustomerVacations_OverlapCheck]
    ON [dbo].[CustomerVacations]([CustomerId], [Status], [StartDate], [EndDate], [Shift])
    INCLUDE ([ResumedDate]);
END
GO


-- ====================================================================================
-- 6. Stored Procedures
-- ====================================================================================

-- 6.1 usp_Customer_Deactivate: Standard deactivation on date D (Last delivery D-1)
CREATE OR ALTER PROCEDURE [dbo].[usp_Customer_Deactivate]
    @CustomerId INT,
    @DeactivationDate DATE,
    @Reason NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. Pessimistic concurrency lock on Customer row
    SELECT 1 FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK) WHERE [CustomerId] = @CustomerId;

    -- 2. Idempotency Check: Exit cleanly if already inactive
    IF EXISTS (SELECT 1 FROM [dbo].[Customers] WHERE [CustomerId] = @CustomerId AND [Status] = 'Inactive' AND [IsActive] = 0)
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    -- 3. Verify no delivery exists on or after DeactivationDate
    IF EXISTS (
        SELECT 1 FROM [dbo].[DailyMilkDeliveries]
        WHERE [CustomerId] = @CustomerId AND [EntryDate] >= @DeactivationDate AND [Status] IN ('Completed', 'Delivered')
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50004, 'Cannot execute standard deactivation: Deliveries already exist on or after deactivation date. Use confirmed deactivation workflow.', 1;
    END

    -- 4. Update Customer Master Status and Soft Delete
    UPDATE [dbo].[Customers]
    SET [Status] = 'Inactive',
        [IsActive] = 0,
        [CurrentDeliveryBoyId] = NULL,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId;

    -- 5. Close Active Delivery Boy Assignment (Safe Boundary Clamping)
    -- If AssignedFrom < DeactivationDate, end on DeactivationDate - 1.
    -- If AssignedFrom == DeactivationDate (same-day onboarding), end on AssignedFrom (D) with IsActive=0 to satisfy CHECK constraint.
    UPDATE [dbo].[CustomerDeliveryBoyAssignments]
    SET [AssignedTo] = CASE
            WHEN [AssignedFrom] < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE [AssignedFrom]
        END,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId,
        [Notes] = CASE
            WHEN [AssignedFrom] >= @DeactivationDate THEN ISNULL([Notes] + ' | ', '') + 'Cancelled on start date (same-day deactivation)'
            ELSE [Notes]
        END
    WHERE [CustomerId] = @CustomerId AND [AssignedTo] IS NULL;

    -- 6. Close Active Subscriptions (Safe Boundary Clamping)
    UPDATE [dbo].[CustomerSubscriptions]
    SET [EffectiveTo] = CASE
            WHEN [EffectiveFrom] < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE [EffectiveFrom]
        END,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 7. Close Active Custom Pricing Overrides (Safe Boundary Clamping)
    UPDATE [dbo].[CustomerCustomPricing]
    SET [EffectiveTo] = CASE
            WHEN [EffectiveFrom] < @DeactivationDate THEN DATEADD(day, -1, @DeactivationDate)
            ELSE [EffectiveFrom]
        END,
        [IsActive] = 0
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 8. Cancel Future Scheduled Vacations
    UPDATE [dbo].[CustomerVacations]
    SET [Status] = 'Cancelled',
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND [Status] IN ('Pending', 'Approved') AND [StartDate] >= @DeactivationDate;

    -- 9. Insert Audit Log
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
    VALUES (@AdminUserId, 'DEACTIVATE_CUSTOMER', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
            CONCAT('{"DeactivationDate":"', CONVERT(VARCHAR(10), @DeactivationDate, 120), '","Reason":"', ISNULL(@Reason, ''), '"}'),
            SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- 6.2 usp_Customer_Deactivate_WithRecordedDelivery: Deactivation when delivery already recorded on date D
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

    -- 3. Close Active Assignment ending ON DeactivationDate D (Preserves Today's Commission & Run)
    UPDATE [dbo].[CustomerDeliveryBoyAssignments]
    SET [AssignedTo] = @DeactivationDate,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId,
        [Notes] = ISNULL([Notes] + ' | ', '') + 'Closed on delivery date; delivery billed; service terminated'
    WHERE [CustomerId] = @CustomerId AND [AssignedTo] IS NULL;

    -- 4. Close Active Subscriptions ending ON DeactivationDate D (Billed through Date D)
    UPDATE [dbo].[CustomerSubscriptions]
    SET [EffectiveTo] = @DeactivationDate,
        [IsActive] = 0,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 5. Close Custom Pricing ending ON DeactivationDate D
    UPDATE [dbo].[CustomerCustomPricing]
    SET [EffectiveTo] = @DeactivationDate,
        [IsActive] = 0
    WHERE [CustomerId] = @CustomerId AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @DeactivationDate);

    -- 6. Cancel Future Scheduled Vacations starting after Date D
    UPDATE [dbo].[CustomerVacations]
    SET [Status] = 'Cancelled',
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId AND [Status] IN ('Pending', 'Approved') AND [StartDate] > @DeactivationDate;

    -- 7. Audit Log with Explicit Preserved Delivery Flag
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
    VALUES (@AdminUserId, 'DEACTIVATE_CONFIRMED_WITH_DELIVERY', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
            CONCAT('{"DeactivationDate":"', CONVERT(VARCHAR(10), @DeactivationDate, 120), '","BilledDeliveryPreserved":1,"Reason":"', ISNULL(@Reason, ''), '"}'),
            SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- 6.3 usp_Customer_Reactivate: Reactivate customer starting on date R (Creates brand new records)
CREATE OR ALTER PROCEDURE [dbo].[usp_Customer_Reactivate]
    @CustomerId INT,
    @ReactivationDate DATE, -- Must be >= CAST(GETDATE() AS DATE)
    @RouteId INT,
    @DeliverySequence INT,
    @DeliveryBoyId INT,
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@ReactivationDate < CAST(GETDATE() AS DATE))
    BEGIN
        THROW 50005, 'Reactivation date cannot be in the past.', 1;
    END

    BEGIN TRANSACTION;

    -- 1. Lock Customer Row
    SELECT 1 FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK) WHERE [CustomerId] = @CustomerId;

    -- 2. Verify Customer is Currently Inactive
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Customers] WHERE [CustomerId] = @CustomerId AND [Status] = 'Inactive')
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50006, 'Customer is not in Inactive status.', 1;
    END

    -- 3. Verify Route and DeliveryBoy
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Routes] WHERE [RouteId] = @RouteId AND [IsActive] = 1)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50007, 'Invalid or inactive route.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM [dbo].[DeliveryBoys] WHERE [DeliveryBoyId] = @DeliveryBoyId AND [IsActive] = 1)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50008, 'Invalid or inactive delivery boy.', 1;
    END

    -- 4. Update Customer Master Record
    UPDATE [dbo].[Customers]
    SET [Status] = 'Active',
        [IsActive] = 1,
        [RouteId] = @RouteId,
        [DeliverySequence] = @DeliverySequence,
        [CurrentDeliveryBoyId] = @DeliveryBoyId,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId;

    -- 5. Insert BRAND NEW Delivery Boy Assignment Record (Past Assignments Remain Immutable)
    INSERT INTO [dbo].[CustomerDeliveryBoyAssignments]
        ([CustomerId], [DeliveryBoyId], [AssignedFrom], [AssignedTo], [IsActive], [Notes], [CreatedAt], [CreatedBy])
    VALUES
        (@CustomerId, @DeliveryBoyId, @ReactivationDate, NULL, 1, @Notes, SYSUTCDATETIME(), @AdminUserId);

    -- 6. Audit Log
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
    VALUES (@AdminUserId, 'REACTIVATE_CUSTOMER', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
            CONCAT('{"ReactivationDate":"', CONVERT(VARCHAR(10), @ReactivationDate, 120), '","RouteId":', @RouteId, ',"DeliveryBoyId":', @DeliveryBoyId, '}'),
            SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- 6.4 usp_Customer_ReassignDeliveryBoy: Concurrency-safe delivery boy reassignment implementing Section 7.2 cases
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

    -- Case 1: Assignment started today and no delivery recorded today -> In-place clerical correction
    IF (@ActiveAssignmentId IS NOT NULL AND @CurrentAssignedFrom = @EffectiveDate AND @EffectiveDate = @Today AND @DeliveryExistsToday = 0)
    BEGIN
        IF (@NewDeliveryBoyId IS NOT NULL)
        BEGIN
            UPDATE [dbo].[CustomerDeliveryBoyAssignments]
            SET [DeliveryBoyId] = @NewDeliveryBoyId,
                [Notes] = ISNULL([Notes] + ' | ', '') + 'Admin clerical correction on start date',
                [UpdatedAt] = SYSUTCDATETIME(),
                [UpdatedBy] = @AdminUserId
            WHERE [AssignmentId] = @ActiveAssignmentId;
        END
        ELSE
        BEGIN
            -- Unassigning on start date
            UPDATE [dbo].[CustomerDeliveryBoyAssignments]
            SET [AssignedTo] = @EffectiveDate,
                [IsActive] = 0,
                [Notes] = ISNULL([Notes] + ' | ', '') + 'Unassigned on start date before deliveries',
                [UpdatedAt] = SYSUTCDATETIME(),
                [UpdatedBy] = @AdminUserId
            WHERE [AssignmentId] = @ActiveAssignmentId;
        END

        UPDATE [dbo].[Customers]
        SET [CurrentDeliveryBoyId] = @NewDeliveryBoyId,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [CustomerId] = @CustomerId;

        INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
        VALUES (@AdminUserId, 'REASSIGN_CLERICAL_CORRECTION_INPLACE', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
                CONCAT('{"NewDeliveryBoyId":', ISNULL(CAST(@NewDeliveryBoyId AS VARCHAR(10)), 'null'), ',"EffectiveDate":"', CONVERT(VARCHAR(10), @EffectiveDate, 120), '"}'),
                SYSUTCDATETIME());

        COMMIT TRANSACTION;
        RETURN;
    END

    -- If EffectiveDate <= CurrentAssignedFrom and we didn't qualify for Case 1, it must be rejected
    IF (@ActiveAssignmentId IS NOT NULL AND @EffectiveDate <= @CurrentAssignedFrom)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50005, 'Reassignment effective date must be after the current assignment start date when in-place correction is not eligible.', 1;
    END

    -- Case 2: Standard Forward Reassignment (@EffectiveDate > @CurrentAssignedFrom)
    IF (@ActiveAssignmentId IS NOT NULL)
    BEGIN
        -- Close previous active assignment on day before EffectiveDate
        UPDATE [dbo].[CustomerDeliveryBoyAssignments]
        SET [AssignedTo] = DATEADD(day, -1, @EffectiveDate),
            [IsActive] = 0,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [AssignmentId] = @ActiveAssignmentId;
    END

    -- If assigning a new delivery boy, insert new assignment row
    IF (@NewDeliveryBoyId IS NOT NULL)
    BEGIN
        INSERT INTO [dbo].[CustomerDeliveryBoyAssignments]
            ([CustomerId], [DeliveryBoyId], [AssignedFrom], [AssignedTo], [IsActive], [Notes], [CreatedAt], [CreatedBy])
        VALUES
            (@CustomerId, @NewDeliveryBoyId, @EffectiveDate, NULL, 1, @Notes, SYSUTCDATETIME(), @AdminUserId);
    END

    -- Update Customer Master
    UPDATE [dbo].[Customers]
    SET [CurrentDeliveryBoyId] = @NewDeliveryBoyId,
        [UpdatedAt] = SYSUTCDATETIME(),
        [UpdatedBy] = @AdminUserId
    WHERE [CustomerId] = @CustomerId;

    -- Audit Log
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
    VALUES (@AdminUserId, 'REASSIGN_DELIVERY_BOY_FORWARD', 'Customers', CAST(@CustomerId AS VARCHAR(50)),
            CONCAT('{"NewDeliveryBoyId":', ISNULL(CAST(@NewDeliveryBoyId AS VARCHAR(10)), 'null'), ',"EffectiveDate":"', CONVERT(VARCHAR(10), @EffectiveDate, 120), '"}'),
            SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- 6.5 usp_CustomerVacation_Create: Concurrency-safe vacation registration with 6-pair conflict matrix check
CREATE OR ALTER PROCEDURE [dbo].[usp_CustomerVacation_Create]
    @CustomerId INT,
    @ReportedByDeliveryBoyId INT = NULL,
    @StartDate DATE,
    @EndDate DATE,
    @Shift NVARCHAR(20) = 'Both',
    @Reason NVARCHAR(250) = NULL,
    @Status NVARCHAR(20) = 'Pending',
    @ApprovedBy INT = NULL,
    @AdminUserId INT,
    @VacationId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validate Dates
    IF (@EndDate < @StartDate)
    BEGIN
        THROW 50009, 'Vacation end date cannot be earlier than start date.', 1;
    END

    -- Validate Shift
    IF (@Shift NOT IN ('Morning', 'Evening', 'Both'))
    BEGIN
        THROW 50010, 'Invalid vacation shift specified.', 1;
    END

    BEGIN TRANSACTION;

    -- 1. Pessimistic concurrency lock on Customer row
    SELECT 1 FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK) WHERE [CustomerId] = @CustomerId;

    -- 2. Verify Customer Exists and is Active
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Customers] WHERE [CustomerId] = @CustomerId AND [IsActive] = 1)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50011, 'Target customer does not exist or is inactive.', 1;
    END

    -- 3. Vacation Overlap Conflict Matrix Check
    -- Conflicting conditions: Both conflicts with Both, Morning, Evening.
    -- Morning conflicts with Morning and Both.
    -- Evening conflicts with Evening and Both.
    -- Morning and Evening may independently coexist.
    IF EXISTS (
        SELECT 1 FROM [dbo].[CustomerVacations] WITH (UPDLOCK)
        WHERE [CustomerId] = @CustomerId
          AND [Status] IN ('Pending', 'Approved')
          AND ([Shift] = 'Both' OR @Shift = 'Both' OR [Shift] = @Shift)
          AND ([StartDate] <= @EndDate AND [EndDate] >= @StartDate)
          AND ([ResumedDate] IS NULL OR @StartDate < [ResumedDate])
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50001, 'An active or pending vacation already covers this customer and shift for the requested dates.', 1;
    END

    -- 4. Insert Vacation Record
    DECLARE @ApprovedAt DATETIME2(7) = NULL;
    IF (@Status = 'Approved')
    BEGIN
        SET @ApprovedAt = SYSUTCDATETIME();
    END

    INSERT INTO [dbo].[CustomerVacations]
        ([CustomerId], [ReportedByDeliveryBoyId], [StartDate], [EndDate], [Shift], [Reason], [Status], [ApprovedBy], [ApprovedAt], [CreatedAt], [CreatedBy])
    VALUES
        (@CustomerId, @ReportedByDeliveryBoyId, @StartDate, @EndDate, @Shift, @Reason, @Status, @ApprovedBy, @ApprovedAt, SYSUTCDATETIME(), @AdminUserId);

    SET @VacationId = SCOPE_IDENTITY();

    -- 5. Audit Log
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
    VALUES (@AdminUserId, 'CREATE_VACATION', 'CustomerVacations', CAST(@VacationId AS VARCHAR(50)),
            CONCAT('{"CustomerId":', @CustomerId, ',"StartDate":"', CONVERT(VARCHAR(10), @StartDate, 120), '","EndDate":"', CONVERT(VARCHAR(10), @EndDate, 120), '","Shift":"', @Shift, '","Status":"', @Status, '"}'),
            SYSUTCDATETIME());

    COMMIT TRANSACTION;
END;
GO

-- 6.6 usp_CustomerSubscription_UpdateSchedule: Atomic, coordinated transition for legitimate schedule updates
CREATE OR ALTER PROCEDURE [dbo].[usp_CustomerSubscription_UpdateSchedule]
    @SubscriptionId INT,
    @NewSchedule NVARCHAR(20),
    @NewDays VARCHAR(20) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 1. Validate inputs
    IF (@NewSchedule NOT IN ('Daily', 'Weekdays', 'Weekends', 'CustomDays'))
    BEGIN
        THROW 50020, 'Invalid delivery schedule specified.', 1;
    END

    -- Normalize days according to schedule
    DECLARE @CanonicalSchedule NVARCHAR(20) = @NewSchedule;
    DECLARE @CanonicalDays VARCHAR(20) = @NewDays;

    IF (@NewSchedule = 'Daily')
    BEGIN
        SET @CanonicalDays = '1,2,3,4,5,6,7';
    END
    ELSE IF (@NewSchedule = 'Weekdays')
    BEGIN
        SET @CanonicalDays = '1,2,3,4,5';
    END
    ELSE IF (@NewSchedule = 'Weekends')
    BEGIN
        SET @CanonicalDays = '6,7';
    END
    ELSE IF (@NewSchedule = 'CustomDays')
    BEGIN
        IF (@CanonicalDays IS NULL OR LEN(@CanonicalDays) = 0)
        BEGIN
            THROW 50021, 'CustomDays schedule requires a non-empty list of delivery days.', 1;
        END
    END

    -- Parse canonical days into a table variable
    DECLARE @TargetDays TABLE (DayOfWeek TINYINT PRIMARY KEY);
    INSERT INTO @TargetDays (DayOfWeek)
    SELECT DISTINCT CAST(value AS TINYINT)
    FROM STRING_SPLIT(@CanonicalDays, ',')
    WHERE RTRIM(LTRIM(value)) != '';

    IF ((SELECT COUNT(1) FROM @TargetDays) = 0)
    BEGIN
        THROW 50021, 'Schedule must specify at least one valid delivery day.', 1;
    END

    IF EXISTS (SELECT 1 FROM @TargetDays WHERE DayOfWeek < 1 OR DayOfWeek > 7)
    BEGIN
        THROW 50022, 'Invalid weekday value. Weekdays must be between 1 and 7.', 1;
    END

    -- Re-generate canonical sorted string
    SELECT @CanonicalDays = STRING_AGG(CAST(DayOfWeek AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY DayOfWeek ASC)
    FROM @TargetDays;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 2. Lock Parent Subscription Row
        SELECT 1 FROM [dbo].[CustomerSubscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [SubscriptionId] = @SubscriptionId;

        IF NOT EXISTS (SELECT 1 FROM [dbo].[CustomerSubscriptions] WHERE [SubscriptionId] = @SubscriptionId AND [IsActive] = 1)
        BEGIN
            THROW 50023, 'Target subscription does not exist or is inactive.', 1;
        END

        -- 3. Set Session Context to allow atomic multi-table transition
        EXEC sys.sp_set_session_context @key = N'SubscriptionScheduleTransition', @value = 1;

        -- 4. Update Parent Subscription
        UPDATE [dbo].[CustomerSubscriptions]
        SET [DeliverySchedule] = @CanonicalSchedule,
            [DeliveryDays] = @CanonicalDays,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [SubscriptionId] = @SubscriptionId;

        -- 5. Synchronize Child Rows (Delete old days and insert new days)
        DELETE FROM [dbo].[CustomerSubscriptionDeliveryDays]
        WHERE [SubscriptionId] = @SubscriptionId;

        INSERT INTO [dbo].[CustomerSubscriptionDeliveryDays] ([SubscriptionId], [DayOfWeek])
        SELECT @SubscriptionId, [DayOfWeek] FROM @TargetDays ORDER BY [DayOfWeek] ASC;

        -- 6. Enforce Exact Set Equality before committing
        DECLARE @ActualChildDays VARCHAR(20);
        SELECT @ActualChildDays = STRING_AGG(CAST([DayOfWeek] AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY [DayOfWeek] ASC)
        FROM [dbo].[CustomerSubscriptionDeliveryDays]
        WHERE [SubscriptionId] = @SubscriptionId;

        IF (@ActualChildDays IS NULL OR @ActualChildDays != @CanonicalDays)
        BEGIN
            THROW 50024, 'Schedule update failed: Child weekdays do not exactly match parent DeliveryDays schedule.', 1;
        END

        -- Reset Session Context BEFORE commit
        EXEC sys.sp_set_session_context @key = N'SubscriptionScheduleTransition', @value = NULL;

        -- 7. Audit Log
        INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [TableName], [RecordId], [NewValuesJson], [Timestamp])
        VALUES (@AdminUserId, 'UPDATE_SUBSCRIPTION_SCHEDULE', 'CustomerSubscriptions', CAST(@SubscriptionId AS VARCHAR(50)),
                CONCAT('{"NewSchedule":"', @CanonicalSchedule, '","NewDays":"', @CanonicalDays, '"}'),
                SYSUTCDATETIME());

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- Guarantee session context cleanup on error, constraint failure, or rollback
        EXEC sys.sp_set_session_context @key = N'SubscriptionScheduleTransition', @value = NULL;
        IF @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 7. Security Hardening: Least-Privilege Application Role (db_dms_app)
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'db_dms_app' AND type = 'R')
BEGIN
    PRINT 'Creating application role db_dms_app...';
    CREATE ROLE [db_dms_app];
END
GO

-- 7.1 Grant read and general transactional DML permissions
GRANT SELECT ON SCHEMA::dbo TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[Customers] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[CustomerDeliveryBoyAssignments] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[CustomerVacations] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[CustomerCustomPricing] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[DailyMilkDeliveries] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[DailyMilkDeliveryItems] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[MonthlyBills] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[MonthlyBillItems] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[Payments] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[PaymentAllocations] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[Users] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[AuditLogs] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[Notifications] TO [db_dms_app];
GRANT INSERT, UPDATE, DELETE ON [dbo].[NotificationAcknowledgements] TO [db_dms_app];

-- 7.2 Subscription Table Access (Least-Privilege Scoping)
-- Allow INSERT for new subscription onboarding (exact set equality enforced by insert trigger)
GRANT INSERT ON [dbo].[CustomerSubscriptions] TO [db_dms_app];
GRANT INSERT ON [dbo].[CustomerSubscriptionDeliveryDays] TO [db_dms_app];

-- Allow UPDATE on non-schedule lifecycle columns (EndSubscriptionAsync, customer deactivation, etc.)
GRANT UPDATE ON [dbo].[CustomerSubscriptions]([EffectiveTo], [IsActive], [UpdatedAt], [UpdatedBy]) TO [db_dms_app];

-- Explicitly DENY direct schedule modification to block trigger bypass outside approved procedure
DENY UPDATE ON [dbo].[CustomerSubscriptions]([DeliverySchedule], [DeliveryDays]) TO [db_dms_app];
DENY DELETE, UPDATE ON [dbo].[CustomerSubscriptionDeliveryDays] TO [db_dms_app];

-- 7.3 Stored Procedure Execution (Authorized Transitions via Ownership Chaining)
GRANT EXECUTE ON [dbo].[usp_CustomerSubscription_UpdateSchedule] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Customer_Deactivate] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Customer_Deactivate_WithRecordedDelivery] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Customer_Reactivate] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Customer_ReassignDeliveryBoy] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_CustomerVacation_Create] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_AllocatePayment] TO [db_dms_app];
GO

-- 7.4 Dedicated Application Database Principal (WITHOUT LOGIN)
-- Used for least-privilege execution context enforcement and deployment role binding
IF USER_ID('dms_app_user') IS NOT NULL
BEGIN
    DROP USER [dms_app_user];
END
GO

PRINT 'Creating application database user dms_app_user (WITHOUT LOGIN)...';
CREATE USER [dms_app_user] WITHOUT LOGIN;
ALTER ROLE [db_dms_app] ADD MEMBER [dms_app_user];
GO

PRINT '====================================================================================';
PRINT '06_CustomerManagementEnhancements migration completed successfully.';
PRINT '====================================================================================';
GO
