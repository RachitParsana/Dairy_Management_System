-- ====================================================================================
-- Script: 11_FinalSystemIntegration.sql
-- Description: Day 7 Final System Integration Database Schema & Stored Procedures
-- Modules: Dashboard, Reports & Analytics, Export Management, Notification Management,
--          System Settings & Audit, Delivery Boy Customer Roster.
-- Target DB: DairyManagementDb (Microsoft SQL Server 2019/2022)
-- Author: Kevin Gajera (Senior SQL Server & ADO.NET Backend Engineer)
-- Baseline: dc1bb6068f4ce4e84979f9acb23d75f062790356 (v1.2.0 Approved Contract)
-- ====================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

USE [DairyManagementDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting Day 7 Final System Integration database deployment...';
PRINT '====================================================================================';
GO

-- ====================================================================================
-- 1. SUPPORTING TABLES & INDEXES
-- ====================================================================================

-- 1.1 NotificationPopupSeen Table (Persists one-time publication popup dismissal across logins)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'NotificationPopupSeen')
BEGIN
    PRINT 'Creating table dbo.NotificationPopupSeen...';
    CREATE TABLE [dbo].[NotificationPopupSeen] (
        [PopupSeenId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_NotificationPopupSeen] PRIMARY KEY CLUSTERED,
        [NotificationId] BIGINT NOT NULL CONSTRAINT [FK_NotificationPopupSeen_NotificationId] REFERENCES [dbo].[Notifications]([NotificationId]) ON DELETE CASCADE,
        [UserId] INT NOT NULL CONSTRAINT [FK_NotificationPopupSeen_UserId] REFERENCES [dbo].[Users]([UserId]),
        [SeenAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationPopupSeen_SeenAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [UQ_NotificationPopupSeen_Notification_User] UNIQUE NONCLUSTERED ([NotificationId], [UserId])
    );
    PRINT 'Table dbo.NotificationPopupSeen created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.NotificationPopupSeen already exists.';
END
GO

-- 1.2 Performance Index for Notification Popup Check
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_NotificationPopupSeen_User' AND object_id = OBJECT_ID(N'[dbo].[NotificationPopupSeen]'))
BEGIN
    PRINT 'Creating performance index IX_NotificationPopupSeen_User...';
    CREATE NONCLUSTERED INDEX [IX_NotificationPopupSeen_User]
    ON [dbo].[NotificationPopupSeen]([UserId], [NotificationId])
    INCLUDE ([SeenAt]);
END
GO

-- 1.3 Unique Filtered Index for Bill Publication Notification Idempotency
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_BillId_Type' AND object_id = OBJECT_ID(N'[dbo].[Notifications]'))
BEGIN
    PRINT 'Creating unique index IX_Notifications_BillId_Type on dbo.Notifications...';
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Notifications_BillId_Type]
    ON [dbo].[Notifications]([BillId], [NotificationType])
    WHERE [BillId] IS NOT NULL;
END
GO

-- ====================================================================================
-- 2. DASHBOARD STORED PROCEDURES
-- ====================================================================================

-- 2.1 Admin Dashboard Metrics
-- Returns 3 result sets in a single round-trip:
-- Set 1: High-level KPI counters and approved month-based unpaid balance ageing.
-- Set 2: Today's delivery shift breakdown (Morning vs. Evening).
-- Set 3: Recent operational delivery ledger (Top 7 rows).
CREATE OR ALTER PROCEDURE [dbo].[usp_Dashboard_GetAdminMetrics]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @CurrentMonth TINYINT = MONTH(@Today);
    DECLARE @CurrentYear SMALLINT = YEAR(@Today);

    -- RESULT SET 1: KPI Summary & Month-Based Ageing
    DECLARE @TotalCust INT = (SELECT COUNT(1) FROM [dbo].[Customers]);
    DECLARE @ActiveCust INT = (SELECT COUNT(1) FROM [dbo].[Customers] WHERE [IsActive] = 1);
    DECLARE @ActiveDB INT = (SELECT COUNT(1) FROM [dbo].[DeliveryBoys] WHERE [IsActive] = 1);

    -- Today's Deliveries
    DECLARE @TodayLitres DECIMAL(18,3) = 0.000;
    DECLARE @TodayCompleted INT = 0;
    DECLARE @TodayPending INT = 0;

    SELECT 
        @TodayLitres = ISNULL(SUM(CASE WHEN [Status] = 'Completed' THEN [TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000),
        @TodayCompleted = ISNULL(SUM(CASE WHEN [Status] = 'Completed' THEN 1 ELSE 0 END), 0),
        @TodayPending = ISNULL(SUM(CASE WHEN [Status] = 'Pending' THEN 1 ELSE 0 END), 0)
    FROM [dbo].[DailyMilkDeliveries]
    WHERE [EntryDate] = @Today;

    -- Current Month Financials (Published Bills only)
    DECLARE @CurMonthBilled DECIMAL(18,2) = 0.00;
    DECLARE @CurMonthCollected DECIMAL(18,2) = 0.00;
    DECLARE @TotalOutstanding DECIMAL(18,2) = 0.00;
    DECLARE @UnpaidBillsCount INT = 0;

    SELECT 
        @CurMonthBilled = ISNULL(SUM(CASE WHEN [BillingMonth] = @CurrentMonth AND [BillingYear] = @CurrentYear THEN [CurrentMonthInvoiceAmount] ELSE 0.00 END), 0.00),
        @TotalOutstanding = ISNULL(SUM([BalanceAmount]), 0.00),
        @UnpaidBillsCount = ISNULL(SUM(CASE WHEN [PaymentStatus] IN ('Unpaid', 'Partially Paid', 'Overdue') THEN 1 ELSE 0 END), 0)
    FROM [dbo].[MonthlyBills]
    WHERE [IsPublished] = 1 AND [BalanceAmount] > 0;

    -- Current Month Collections (Valid non-voided payments recorded this month)
    SELECT 
        @CurMonthCollected = ISNULL(SUM([Amount]), 0.00)
    FROM [dbo].[Payments]
    WHERE [IsVoided] = 0 AND MONTH([PaymentDate]) = @CurrentMonth AND YEAR([PaymentDate]) = @CurrentYear;

    -- Approved Month-Based Ageing:
    -- Calculated strictly using unpaid invoice portions (BalanceAmount)
    -- Buckets: 1 month, 2 months, 3 months, 4-5 months, 6+ months
    DECLARE @Age1Month DECIMAL(18,2) = 0.00;
    DECLARE @Age2Months DECIMAL(18,2) = 0.00;
    DECLARE @Age3Months DECIMAL(18,2) = 0.00;
    DECLARE @Age4To5Months DECIMAL(18,2) = 0.00;
    DECLARE @Age6PlusMonths DECIMAL(18,2) = 0.00;

    SELECT 
        @Age1Month = ISNULL(SUM(CASE WHEN MonthDiff = 1 THEN [BalanceAmount] ELSE 0.00 END), 0.00),
        @Age2Months = ISNULL(SUM(CASE WHEN MonthDiff = 2 THEN [BalanceAmount] ELSE 0.00 END), 0.00),
        @Age3Months = ISNULL(SUM(CASE WHEN MonthDiff = 3 THEN [BalanceAmount] ELSE 0.00 END), 0.00),
        @Age4To5Months = ISNULL(SUM(CASE WHEN MonthDiff BETWEEN 4 AND 5 THEN [BalanceAmount] ELSE 0.00 END), 0.00),
        @Age6PlusMonths = ISNULL(SUM(CASE WHEN MonthDiff >= 6 THEN [BalanceAmount] ELSE 0.00 END), 0.00)
    FROM (
        SELECT 
            b.[BalanceAmount],
            ((@CurrentYear - b.[BillingYear]) * 12 + (@CurrentMonth - b.[BillingMonth])) AS MonthDiff
        FROM [dbo].[MonthlyBills] b
        WHERE b.[IsPublished] = 1 AND b.[BalanceAmount] > 0
    ) aged;

    SELECT 
        @TotalCust AS TotalCustomers,
        @ActiveCust AS ActiveCustomers,
        @ActiveDB AS ActiveDeliveryBoys,
        @TodayLitres AS TodayDeliveredLitres,
        @TodayCompleted AS TodayCompletedDrops,
        @TodayPending AS TodayPendingDrops,
        @CurMonthBilled AS CurrentMonthBillingTotal,
        @CurMonthCollected AS CurrentMonthCollectedTotal,
        @TotalOutstanding AS TotalOutstandingBalance,
        @UnpaidBillsCount AS UnpaidInvoicesCount,
        @Age1Month AS UnpaidBalance1Month,
        @Age2Months AS UnpaidBalance2Months,
        @Age3Months AS UnpaidBalance3Months,
        @Age4To5Months AS UnpaidBalance4To5Months,
        @Age6PlusMonths AS UnpaidBalance6PlusMonths;

    -- RESULT SET 2: Today's Shift Breakdown
    SELECT 
        s.[Shift],
        ISNULL(SUM(CASE WHEN d.[Status] = 'Completed' THEN d.[TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000) AS DeliveredLitres,
        ISNULL(SUM(CASE WHEN d.[Status] = 'Completed' THEN 1 ELSE 0 END), 0) AS CompletedDeliveries,
        ISNULL(SUM(CASE WHEN d.[Status] = 'Pending' THEN 1 ELSE 0 END), 0) AS PendingDeliveries
    FROM (VALUES ('Morning'), ('Evening')) s([Shift])
    LEFT JOIN [dbo].[DailyMilkDeliveries] d ON d.[Shift] = s.[Shift] AND d.[EntryDate] = @Today
    GROUP BY s.[Shift];

    -- RESULT SET 3: Recent Operational Delivery Ledger (Top 7 rows)
    SELECT TOP (7)
        d.[DeliveryId],
        d.[EntryDate],
        d.[Shift],
        c.[FullName] AS CustomerName,
        r.[RouteName],
        d.[TotalLiquidVolumeLitres],
        d.[TotalAmount],
        d.[Status] AS DeliveryStatus,
        ISNULL((
            SELECT TOP (1) mc.[CategoryName]
            FROM [dbo].[DailyMilkDeliveryItems] di
            JOIN [dbo].[MilkCategories] mc ON di.[CategoryId] = mc.[CategoryId]
            WHERE di.[DeliveryId] = d.[DeliveryId]
            ORDER BY di.[SubTotal] DESC
        ), N'Mixed') AS PrimaryCategoryName
    FROM [dbo].[DailyMilkDeliveries] d
    JOIN [dbo].[Customers] c ON d.[CustomerId] = c.[CustomerId]
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    ORDER BY d.[CreatedAt] DESC, d.[DeliveryId] DESC;
END;
GO

-- 2.2 Delivery Boy Dashboard Metrics
CREATE OR ALTER PROCEDURE [dbo].[usp_Dashboard_GetDeliveryBoyMetrics]
    @DeliveryBoyId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);

    DECLARE @RouteId INT;
    DECLARE @RouteName NVARCHAR(100);
    DECLARE @FullName NVARCHAR(100);
    DECLARE @UserId INT;

    SELECT 
        @FullName = u.[FullName],
        @RouteId = db.[PrimaryRouteId],
        @RouteName = ISNULL(r.[RouteName], N'Unassigned'),
        @UserId = db.[UserId]
    FROM [dbo].[DeliveryBoys] db
    JOIN [dbo].[Users] u ON db.[UserId] = u.[UserId]
    LEFT JOIN [dbo].[Routes] r ON db.[PrimaryRouteId] = r.[RouteId]
    WHERE db.[DeliveryBoyId] = @DeliveryBoyId AND db.[IsActive] = 1;

    IF @FullName IS NULL
    BEGIN
        THROW 50060, 'Delivery Boy not found or inactive.', 1;
    END;

    DECLARE @TotalRouteCust INT = (
        SELECT COUNT(1) 
        FROM [dbo].[Customers] 
        WHERE [CurrentDeliveryBoyId] = @DeliveryBoyId AND [IsActive] = 1
    );

    DECLARE @TodayLitres DECIMAL(18,3) = 0.000;
    DECLARE @CompletedDrops INT = 0;
    DECLARE @PendingDrops INT = 0;

    SELECT 
        @TodayLitres = ISNULL(SUM(CASE WHEN [Status] = 'Completed' THEN [TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000),
        @CompletedDrops = ISNULL(SUM(CASE WHEN [Status] = 'Completed' THEN 1 ELSE 0 END), 0),
        @PendingDrops = ISNULL(SUM(CASE WHEN [Status] = 'Pending' THEN 1 ELSE 0 END), 0)
    FROM [dbo].[DailyMilkDeliveries]
    WHERE [DeliveryBoyId] = @DeliveryBoyId AND [EntryDate] = @Today;

    -- Unread Notifications Count (Targeting All, DeliveryBoy, or this DeliveryBoy/Route)
    DECLARE @UnreadCount INT = (
        SELECT COUNT(1)
        FROM [dbo].[Notifications] n
        WHERE (n.[TargetRole] IN ('All', 'DeliveryBoy'))
          AND (n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId)
          AND (n.[TargetDeliveryBoyId] IS NULL OR n.[TargetDeliveryBoyId] = @DeliveryBoyId)
          AND NOT EXISTS (
              SELECT 1 FROM [dbo].[NotificationAcknowledgements] na 
              WHERE na.[NotificationId] = n.[NotificationId] AND na.[UserId] = @UserId
          )
    );

    SELECT 
        @DeliveryBoyId AS DeliveryBoyId,
        @FullName AS FullName,
        @RouteId AS AssignedRouteId,
        @RouteName AS AssignedRouteName,
        @TotalRouteCust AS TotalRouteCustomers,
        @TodayLitres AS TodayDeliveredLitres,
        @CompletedDrops AS CompletedDropsToday,
        @PendingDrops AS PendingDropsToday,
        @UnreadCount AS UnreadNotificationsCount;
END;
GO

-- ====================================================================================
-- 3. NOTIFICATION STORED PROCEDURES
-- ====================================================================================

-- 3.1 Create Generic / Administrative Notification
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_Create]
    @Title NVARCHAR(100),
    @Message NVARCHAR(1000),
    @NotificationType NVARCHAR(30),
    @TargetRole NVARCHAR(20),
    @TargetRouteId INT = NULL,
    @TargetDeliveryBoyId INT = NULL,
    @CustomerId INT = NULL,
    @BillId BIGINT = NULL,
    @CustomerNameSnapshot NVARCHAR(100) = NULL,
    @UpdatedBillAmount DECIMAL(18,2) = NULL,
    @NewTotalOutstandingBalance DECIMAL(18,2) = NULL,
    @BillingMonthYear VARCHAR(10) = NULL,
    @PendingMonthsCount INT = NULL,
    @AdminUserId INT,
    @NewNotificationId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @NotificationType NOT IN ('BillPublished', 'BillUpdated', 'GeneralAnnouncement', 'VacationUpdate')
        THROW 50061, 'Invalid NotificationType.', 1;

    IF @TargetRole NOT IN ('All', 'DeliveryBoy', 'Admin')
        THROW 50062, 'Invalid TargetRole.', 1;

    INSERT INTO [dbo].[Notifications] (
        [Title], [Message], [NotificationType], [TargetRole],
        [TargetRouteId], [TargetDeliveryBoyId], [CustomerId], [BillId],
        [CustomerNameSnapshot], [UpdatedBillAmount], [NewTotalOutstandingBalance],
        [BillingMonthYear], [PendingMonthsCount], [CreatedAt], [CreatedBy]
    ) VALUES (
        @Title, @Message, @NotificationType, @TargetRole,
        @TargetRouteId, @TargetDeliveryBoyId, @CustomerId, @BillId,
        @CustomerNameSnapshot, @UpdatedBillAmount, @NewTotalOutstandingBalance,
        @BillingMonthYear, @PendingMonthsCount, SYSUTCDATETIME(), @AdminUserId
    );

    SET @NewNotificationId = SCOPE_IDENTITY();
END;
GO

-- 3.2 Emit Bill Published Notification (Strictly Idempotent)
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_EmitBillPublished]
    @BillId BIGINT,
    @AdminUserId INT,
    @NotificationId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NotificationId = 0;

    -- Normalize/resolve AdminUserId if invalid or missing to guarantee FK integrity
    IF @AdminUserId IS NULL OR NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [UserId] = @AdminUserId)
    BEGIN
        SELECT TOP 1 @AdminUserId = [UserId] 
        FROM [dbo].[Users] 
        WHERE [Role] = 'Admin' AND [IsActive] = 1 
        ORDER BY [UserId];
    END;

    -- 1. Verify bill exists and is published
    DECLARE @CustomerId INT, @InvoiceAmount DECIMAL(18,2), @BalanceAmount DECIMAL(18,2);
    DECLARE @BillingMonth TINYINT, @BillingYear SMALLINT;
    SELECT 
        @CustomerId = b.[CustomerId],
        @InvoiceAmount = b.[CurrentMonthInvoiceAmount],
        @BalanceAmount = b.[BalanceAmount],
        @BillingMonth = b.[BillingMonth],
        @BillingYear = b.[BillingYear]
    FROM [dbo].[MonthlyBills] b
    WHERE b.[BillId] = @BillId AND b.[IsPublished] = 1;

    IF @CustomerId IS NULL
    BEGIN
        RETURN; -- Bill does not exist or is an unpublished draft
    END;

    -- 2. Enforce strict database idempotency: check if publication alert already emitted
    SELECT @NotificationId = [NotificationId]
    FROM [dbo].[Notifications]
    WHERE [BillId] = @BillId AND [NotificationType] = 'BillPublished';

    IF @NotificationId > 0
    BEGIN
        RETURN; -- Already exists; prevent duplicate insertion
    END;

    -- 3. Resolve customer, route, and assigned delivery boy
    DECLARE @CustomerName NVARCHAR(100), @RouteId INT, @DeliveryBoyId INT, @RouteName NVARCHAR(100);
    SELECT 
        @CustomerName = c.[FullName],
        @RouteId = c.[RouteId],
        @DeliveryBoyId = c.[CurrentDeliveryBoyId],
        @RouteName = r.[RouteName]
    FROM [dbo].[Customers] c
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    WHERE c.[CustomerId] = @CustomerId;

    DECLARE @MonthYearStr VARCHAR(10) = RIGHT('0' + CAST(@BillingMonth AS VARCHAR(2)), 2) + '-' + CAST(@BillingYear AS VARCHAR(4));
    DECLARE @NoticeTitle NVARCHAR(100) = N'Bill Published: ' + @CustomerName;
    DECLARE @NoticeMessage NVARCHAR(1000) = N'Monthly bill for ' + @CustomerName + N' (' + @MonthYearStr + N') published on ' + @RouteName + N'. Amount: ₹' + CAST(@InvoiceAmount AS NVARCHAR(20)) + N'.';

    -- 4. Atomic insertion targeting the route and assigned delivery boy
    INSERT INTO [dbo].[Notifications] (
        [Title], [Message], [NotificationType], [TargetRole], [TargetRouteId], [TargetDeliveryBoyId],
        [CustomerId], [BillId], [CustomerNameSnapshot], [UpdatedBillAmount], [NewTotalOutstandingBalance],
        [BillingMonthYear], [PendingMonthsCount], [CreatedAt], [CreatedBy]
    ) VALUES (
        @NoticeTitle, @NoticeMessage, 'BillPublished', 'DeliveryBoy', @RouteId, @DeliveryBoyId,
        @CustomerId, @BillId, @CustomerName, @InvoiceAmount, @BalanceAmount,
        @MonthYearStr, 1, SYSUTCDATETIME(), @AdminUserId
    );

    SET @NotificationId = SCOPE_IDENTITY();
END;
GO

-- 3.3 Get Notifications for User (Role & Route Aware)
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_GetForUser]
    @UserId INT,
    @Role NVARCHAR(20),
    @DeliveryBoyId INT = NULL,
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        n.[NotificationId],
        n.[Title],
        n.[Message],
        n.[NotificationType],
        n.[TargetRole],
        n.[TargetRouteId],
        r.[RouteName] AS TargetRouteName,
        n.[TargetDeliveryBoyId],
        n.[CustomerId],
        n.[BillId],
        n.[CustomerNameSnapshot],
        n.[UpdatedBillAmount],
        n.[NewTotalOutstandingBalance],
        n.[BillingMonthYear],
        n.[PendingMonthsCount],
        n.[CreatedAt],
        u.[FullName] AS CreatedByName,
        CASE WHEN na.[AcknowledgementId] IS NOT NULL THEN 1 ELSE 0 END AS IsAcknowledged,
        na.[AcknowledgedAt]
    FROM [dbo].[Notifications] n
    LEFT JOIN [dbo].[Routes] r ON n.[TargetRouteId] = r.[RouteId]
    LEFT JOIN [dbo].[Users] u ON n.[CreatedBy] = u.[UserId]
    LEFT JOIN [dbo].[NotificationAcknowledgements] na 
           ON n.[NotificationId] = na.[NotificationId] AND na.[UserId] = @UserId
    WHERE (n.[TargetRole] = 'All' OR n.[TargetRole] = @Role)
      AND (n.[TargetRouteId] IS NULL OR n.[TargetRouteId] = @RouteId)
      AND (n.[TargetDeliveryBoyId] IS NULL OR n.[TargetDeliveryBoyId] = @DeliveryBoyId)
    ORDER BY n.[CreatedAt] DESC, n.[NotificationId] DESC;
END;
GO

-- 3.4 Acknowledge Notification (Read/Seen Receipt Only)
-- STRICT INVARIANT: Has ZERO payment, collection, or balance modifying effect.
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_Acknowledge]
    @NotificationId BIGINT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[Notifications] WHERE [NotificationId] = @NotificationId)
        THROW 50063, 'Notification not found.', 1;

    -- Record read/seen acknowledgement if not already recorded
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[NotificationAcknowledgements] 
        WHERE [NotificationId] = @NotificationId AND [UserId] = @UserId
    )
    BEGIN
        INSERT INTO [dbo].[NotificationAcknowledgements] ([NotificationId], [UserId], [AcknowledgedAt])
        VALUES (@NotificationId, @UserId, SYSUTCDATETIME());
    END;

    -- Also mark popup seen so modal never reappears
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[NotificationPopupSeen]
        WHERE [NotificationId] = @NotificationId AND [UserId] = @UserId
    )
    BEGIN
        INSERT INTO [dbo].[NotificationPopupSeen] ([NotificationId], [UserId], [SeenAt])
        VALUES (@NotificationId, @UserId, SYSUTCDATETIME());
    END;
END;
GO

-- 3.5 Retrieve Unacknowledged Bill Publication Popups for Delivery Boy
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_GetUnacknowledgedBillPopups]
    @DeliveryBoyId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RouteId INT = (
        SELECT [PrimaryRouteId] 
        FROM [dbo].[DeliveryBoys] 
        WHERE [DeliveryBoyId] = @DeliveryBoyId AND [IsActive] = 1
    );

    SELECT 
        n.[NotificationId],
        n.[Title],
        n.[Message],
        n.[BillId],
        n.[CustomerId],
        n.[CustomerNameSnapshot],
        n.[UpdatedBillAmount],
        n.[NewTotalOutstandingBalance],
        n.[BillingMonthYear],
        n.[CreatedAt]
    FROM [dbo].[Notifications] n
    WHERE n.[NotificationType] = 'BillPublished'
      AND n.[TargetRole] IN ('All', 'DeliveryBoy')
      AND (n.[TargetDeliveryBoyId] = @DeliveryBoyId OR (n.[TargetDeliveryBoyId] IS NULL AND n.[TargetRouteId] = @RouteId))
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[NotificationPopupSeen] nps 
          WHERE nps.[NotificationId] = n.[NotificationId] AND nps.[UserId] = @UserId
      )
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[NotificationAcknowledgements] na 
          WHERE na.[NotificationId] = n.[NotificationId] AND na.[UserId] = @UserId
      )
    ORDER BY n.[CreatedAt] DESC;
END;
GO

-- 3.6 Mark Notification Popup as Seen (Persists dismissal across logins)
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_MarkPopupSeen]
    @NotificationId BIGINT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[NotificationPopupSeen]
        WHERE [NotificationId] = @NotificationId AND [UserId] = @UserId
    )
    BEGIN
        INSERT INTO [dbo].[NotificationPopupSeen] ([NotificationId], [UserId], [SeenAt])
        VALUES (@NotificationId, @UserId, SYSUTCDATETIME());
    END;
END;
GO

-- 3.7 Admin Notification Overview
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_GetAdminOverview]
    @Limit INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        n.[NotificationId],
        n.[Title],
        n.[Message],
        n.[NotificationType],
        n.[TargetRole],
        r.[RouteName] AS TargetRouteName,
        udb.[FullName] AS TargetDeliveryBoyName,
        n.[CustomerNameSnapshot],
        n.[UpdatedBillAmount],
        n.[BillingMonthYear],
        n.[CreatedAt],
        u.[FullName] AS CreatedByName,
        (SELECT COUNT(1) FROM [dbo].[NotificationAcknowledgements] na WHERE na.[NotificationId] = n.[NotificationId]) AS AcknowledgementCount
    FROM [dbo].[Notifications] n
    LEFT JOIN [dbo].[Routes] r ON n.[TargetRouteId] = r.[RouteId]
    LEFT JOIN [dbo].[DeliveryBoys] db ON n.[TargetDeliveryBoyId] = db.[DeliveryBoyId]
    LEFT JOIN [dbo].[Users] udb ON db.[UserId] = udb.[UserId]
    LEFT JOIN [dbo].[Users] u ON n.[CreatedBy] = u.[UserId]
    ORDER BY n.[CreatedAt] DESC;
END;
GO

-- 3.8 Reconcile Published Bill Notifications (Durable Pending/Recovery Engine)
CREATE OR ALTER PROCEDURE [dbo].[usp_Notifications_ReconcilePublishedBills]
    @AdminUserId INT = NULL,
    @ReconciledCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ReconciledCount = 0;

    -- Normalize/resolve AdminUserId if invalid or missing to guarantee FK integrity
    IF @AdminUserId IS NULL OR NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [UserId] = @AdminUserId)
    BEGIN
        SELECT TOP 1 @AdminUserId = [UserId] 
        FROM [dbo].[Users] 
        WHERE [Role] = 'Admin' AND [IsActive] = 1 
        ORDER BY [UserId];
    END;

    -- Query all published bills that lack an active BillPublished notification
    DECLARE @PendingBills TABLE (
        [BillId] BIGINT PRIMARY KEY
    );

    INSERT INTO @PendingBills ([BillId])
    SELECT b.[BillId]
    FROM [dbo].[MonthlyBills] b
    WHERE b.[IsPublished] = 1
      AND NOT EXISTS (
          SELECT 1 
          FROM [dbo].[Notifications] n 
          WHERE n.[BillId] = b.[BillId] 
            AND n.[NotificationType] = 'BillPublished'
      );

    DECLARE @CurBillId BIGINT;
    DECLARE @CreatedNotifId BIGINT;

    DECLARE bill_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT [BillId] FROM @PendingBills;

    OPEN bill_cursor;
    FETCH NEXT FROM bill_cursor INTO @CurBillId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC [dbo].[usp_Notifications_EmitBillPublished]
            @BillId = @CurBillId,
            @AdminUserId = @AdminUserId,
            @NotificationId = @CreatedNotifId OUTPUT;

        IF @CreatedNotifId > 0
        BEGIN
            SET @ReconciledCount = @ReconciledCount + 1;
        END;

        FETCH NEXT FROM bill_cursor INTO @CurBillId;
    END;

    CLOSE bill_cursor;
    DEALLOCATE bill_cursor;
END;
GO

-- ====================================================================================
-- 4. REPORTING STORED PROCEDURES
-- ====================================================================================

-- 4.1 Monthly Billing vs. Actual Collection Trends
CREATE OR ALTER PROCEDURE [dbo].[usp_Reports_GetMonthlyBillingVsCollections]
    @Months INT = 6
AS
BEGIN
    SET NOCOUNT ON;

    IF @Months < 1 OR @Months > 36
        SET @Months = 6;

    DECLARE @CurrentDate DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @StartDate DATE = DATEADD(month, -(@Months - 1), DATEFROMPARTS(YEAR(@CurrentDate), MONTH(@CurrentDate), 1));

    -- Generate calendar month sequence
    ;WITH MonthSeq AS (
        SELECT @StartDate AS PeriodStart
        UNION ALL
        SELECT DATEADD(month, 1, PeriodStart)
        FROM MonthSeq
        WHERE PeriodStart < DATEFROMPARTS(YEAR(@CurrentDate), MONTH(@CurrentDate), 1)
    )
    SELECT 
        CAST(YEAR(m.PeriodStart) AS SMALLINT) AS BillingYear,
        CAST(MONTH(m.PeriodStart) AS TINYINT) AS BillingMonth,
        DATENAME(month, m.PeriodStart) AS MonthName,
        ISNULL((
            SELECT SUM(b.[CurrentMonthInvoiceAmount])
            FROM [dbo].[MonthlyBills] b
            WHERE b.[IsPublished] = 1 
              AND b.[BillingMonth] = MONTH(m.PeriodStart) 
              AND b.[BillingYear] = YEAR(m.PeriodStart)
        ), 0.00) AS InvoicedAmount,
        ISNULL((
            SELECT SUM(p.[Amount])
            FROM [dbo].[Payments] p
            WHERE p.[IsVoided] = 0 
              AND MONTH(p.[PaymentDate]) = MONTH(m.PeriodStart) 
              AND YEAR(p.[PaymentDate]) = YEAR(m.PeriodStart)
        ), 0.00) AS CollectedAmount
    FROM MonthSeq m
    ORDER BY m.PeriodStart ASC
    OPTION (MAXRECURSION 60);
END;
GO

-- 4.2 Route-Wise Milk Consumption Report
CREATE OR ALTER PROCEDURE [dbo].[usp_Reports_GetRouteMilkConsumption]
    @FromDate DATE,
    @ToDate DATE,
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        r.[RouteId],
        r.[RouteName],
        COUNT(DISTINCT c.[CustomerId]) AS ActiveCustomerCount,
        ISNULL(SUM(CASE WHEN d.[Status] = 'Completed' THEN d.[TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000) AS TotalLiquidVolumeLitres,
        ISNULL(SUM(CASE WHEN d.[Status] = 'Completed' AND d.[Shift] = 'Morning' THEN d.[TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000) AS MorningVolumeLitres,
        ISNULL(SUM(CASE WHEN d.[Status] = 'Completed' AND d.[Shift] = 'Evening' THEN d.[TotalLiquidVolumeLitres] ELSE 0.000 END), 0.000) AS EveningVolumeLitres
    FROM [dbo].[Routes] r
    LEFT JOIN [dbo].[Customers] c ON r.[RouteId] = c.[RouteId] AND c.[IsActive] = 1
    LEFT JOIN [dbo].[DailyMilkDeliveries] d 
           ON c.[CustomerId] = d.[CustomerId] 
          AND d.[EntryDate] BETWEEN @FromDate AND @ToDate
    WHERE (@RouteId IS NULL OR r.[RouteId] = @RouteId)
    GROUP BY r.[RouteId], r.[RouteName]
    ORDER BY r.[RouteName] ASC;
END;
GO

-- 4.3 Daily Milk Consumption by Category and Shift
CREATE OR ALTER PROCEDURE [dbo].[usp_Reports_GetDailyCategoryShiftConsumption]
    @FromDate DATE,
    @ToDate DATE,
    @CategoryId INT = NULL,
    @Shift NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        d.[EntryDate],
        mc.[CategoryName],
        d.[Shift],
        mc.[UnitOfMeasure] AS Unit,
        SUM(di.[PackagingQuantity]) AS DeliveredQuantity,
        SUM(di.[SubTotal]) AS TotalMonetaryAmount
    FROM [dbo].[DailyMilkDeliveries] d
    JOIN [dbo].[DailyMilkDeliveryItems] di ON d.[DeliveryId] = di.[DeliveryId]
    JOIN [dbo].[MilkCategories] mc ON di.[CategoryId] = mc.[CategoryId]
    WHERE d.[Status] = 'Completed'
      AND d.[EntryDate] BETWEEN @FromDate AND @ToDate
      AND (@CategoryId IS NULL OR di.[CategoryId] = @CategoryId)
      AND (@Shift IS NULL OR d.[Shift] = @Shift)
    GROUP BY d.[EntryDate], mc.[CategoryName], d.[Shift], mc.[UnitOfMeasure]
    ORDER BY d.[EntryDate] DESC, mc.[CategoryName] ASC, d.[Shift] ASC;
END;
GO

-- 4.4 Customer Outstanding Balance Ageing (Approved Month-Based Contract)
-- Calculates ageing strictly from unpaid invoice portions (BalanceAmount)
-- Buckets: 1 month, 2 months, 3 months, 4-5 months, 6+ months
CREATE OR ALTER PROCEDURE [dbo].[usp_Reports_GetCustomerBalanceAging]
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);
    DECLARE @CurMonth TINYINT = MONTH(@Today);
    DECLARE @CurYear SMALLINT = YEAR(@Today);

    SELECT 
        c.[CustomerId],
        c.[CustomerCode],
        c.[FullName],
        c.[Phone],
        r.[RouteName],
        ISNULL(SUM(CASE WHEN aged.MonthDiff <= 1 THEN aged.[BalanceAmount] ELSE 0.00 END), 0.00) AS UnpaidBalance1Month,
        ISNULL(SUM(CASE WHEN aged.MonthDiff = 2 THEN aged.[BalanceAmount] ELSE 0.00 END), 0.00) AS UnpaidBalance2Months,
        ISNULL(SUM(CASE WHEN aged.MonthDiff = 3 THEN aged.[BalanceAmount] ELSE 0.00 END), 0.00) AS UnpaidBalance3Months,
        ISNULL(SUM(CASE WHEN aged.MonthDiff BETWEEN 4 AND 5 THEN aged.[BalanceAmount] ELSE 0.00 END), 0.00) AS UnpaidBalance4To5Months,
        ISNULL(SUM(CASE WHEN aged.MonthDiff >= 6 THEN aged.[BalanceAmount] ELSE 0.00 END), 0.00) AS UnpaidBalance6PlusMonths,
        ISNULL(SUM(aged.[BalanceAmount]), 0.00) AS TotalOutstanding,
        COUNT(aged.[BillId]) AS UnpaidBillsCount
    FROM [dbo].[Customers] c
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    JOIN (
        SELECT 
            b.[CustomerId],
            b.[BillId],
            b.[BalanceAmount],
            ((@CurYear - b.[BillingYear]) * 12 + (@CurMonth - b.[BillingMonth])) AS MonthDiff
        FROM [dbo].[MonthlyBills] b
        WHERE b.[IsPublished] = 1 AND b.[BalanceAmount] > 0
    ) aged ON c.[CustomerId] = aged.[CustomerId]
    WHERE (@RouteId IS NULL OR c.[RouteId] = @RouteId)
    GROUP BY c.[CustomerId], c.[CustomerCode], c.[FullName], c.[Phone], r.[RouteName]
    HAVING SUM(aged.[BalanceAmount]) > 0
    ORDER BY TotalOutstanding DESC;
END;
GO

-- ====================================================================================
-- 5. EXPORT STORED PROCEDURES
-- ====================================================================================

-- 5.1 Export Customers
CREATE OR ALTER PROCEDURE [dbo].[usp_Export_GetCustomers]
    @RouteId INT = NULL,
    @IsActive BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.[CustomerCode],
        c.[FullName],
        c.[Phone],
        c.[Address],
        c.[Area],
        c.[Landmark],
        r.[RouteName],
        ISNULL(udb.[FullName], N'Unassigned') AS DeliveryBoyName,
        c.[DeliverySequence],
        CASE WHEN c.[IsActive] = 1 THEN N'Active' ELSE N'Inactive' END AS Status,
        ISNULL((
            SELECT SUM(b.[BalanceAmount])
            FROM [dbo].[MonthlyBills] b
            WHERE b.[CustomerId] = c.[CustomerId] AND b.[IsPublished] = 1
        ), 0.00) AS OutstandingBalance,
        c.[CreatedAt]
    FROM [dbo].[Customers] c
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    LEFT JOIN [dbo].[DeliveryBoys] db ON c.[CurrentDeliveryBoyId] = db.[DeliveryBoyId]
    LEFT JOIN [dbo].[Users] udb ON db.[UserId] = udb.[UserId]
    WHERE (@RouteId IS NULL OR c.[RouteId] = @RouteId)
      AND (@IsActive IS NULL OR c.[IsActive] = @IsActive)
    ORDER BY r.[RouteName] ASC, c.[DeliverySequence] ASC, c.[CustomerCode] ASC;
END;
GO

-- 5.2 Export Daily Milk Deliveries
CREATE OR ALTER PROCEDURE [dbo].[usp_Export_GetDailyDeliveries]
    @FromDate DATE,
    @ToDate DATE,
    @RouteId INT = NULL,
    @Shift NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        d.[DeliveryId],
        d.[EntryDate],
        d.[Shift],
        c.[CustomerCode],
        c.[FullName] AS CustomerName,
        r.[RouteName],
        udb.[FullName] AS DeliveryBoyName,
        d.[TotalLiquidVolumeLitres],
        d.[TotalAmount],
        d.[Status],
        ROUND(d.[EligibleLitreQuantity] * d.[CommissionRateSnapshot], 2) AS CommissionEarned,
        d.[CreatedAt]
    FROM [dbo].[DailyMilkDeliveries] d
    JOIN [dbo].[Customers] c ON d.[CustomerId] = c.[CustomerId]
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    JOIN [dbo].[DeliveryBoys] db ON d.[DeliveryBoyId] = db.[DeliveryBoyId]
    JOIN [dbo].[Users] udb ON db.[UserId] = udb.[UserId]
    WHERE d.[EntryDate] BETWEEN @FromDate AND @ToDate
      AND (@RouteId IS NULL OR c.[RouteId] = @RouteId)
      AND (@Shift IS NULL OR d.[Shift] = @Shift)
    ORDER BY d.[EntryDate] DESC, d.[Shift] ASC, r.[RouteName] ASC, c.[CustomerCode] ASC;
END;
GO

-- 5.3 Export Monthly Bills
CREATE OR ALTER PROCEDURE [dbo].[usp_Export_GetMonthlyBills]
    @Month TINYINT,
    @Year SMALLINT,
    @PaymentStatus NVARCHAR(20) = NULL,
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        b.[BillNumber],
        RIGHT('0' + CAST(b.[BillingMonth] AS VARCHAR(2)), 2) + '-' + CAST(b.[BillingYear] AS VARCHAR(4)) AS MonthYear,
        c.[CustomerCode],
        c.[FullName] AS CustomerName,
        r.[RouteName],
        b.[TotalLiquidMilkVolumeLitres],
        b.[CurrentMonthInvoiceAmount],
        b.[PaidAmount],
        b.[BalanceAmount],
        b.[PaymentDueDate],
        b.[PaymentStatus],
        b.[PublishedAt]
    FROM [dbo].[MonthlyBills] b
    JOIN [dbo].[Customers] c ON b.[CustomerId] = c.[CustomerId]
    JOIN [dbo].[Routes] r ON c.[RouteId] = r.[RouteId]
    WHERE b.[BillingMonth] = @Month 
      AND b.[BillingYear] = @Year
      AND b.[IsPublished] = 1
      AND (@PaymentStatus IS NULL OR b.[PaymentStatus] = @PaymentStatus)
      AND (@RouteId IS NULL OR c.[RouteId] = @RouteId)
    ORDER BY r.[RouteName] ASC, c.[CustomerCode] ASC;
END;
GO

-- 5.4 Export Payments
CREATE OR ALTER PROCEDURE [dbo].[usp_Export_GetPayments]
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,
    @PaymentMode NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        p.[ReceiptNumber],
        p.[PaymentDate],
        c.[CustomerCode],
        c.[FullName] AS CustomerName,
        p.[PaymentMode],
        p.[ReferenceNumber],
        p.[Amount],
        u.[FullName] AS ReceivedByAdmin,
        p.[Notes],
        p.[CreatedAt]
    FROM [dbo].[Payments] p
    JOIN [dbo].[Customers] c ON p.[CustomerId] = c.[CustomerId]
    LEFT JOIN [dbo].[Users] u ON p.[ReceivedBy] = u.[UserId]
    WHERE p.[IsVoided] = 0
      AND (@FromDate IS NULL OR p.[PaymentDate] >= @FromDate)
      AND (@ToDate IS NULL OR p.[PaymentDate] <= @ToDate)
      AND (@PaymentMode IS NULL OR p.[PaymentMode] = @PaymentMode)
    ORDER BY p.[PaymentDate] DESC, p.[PaymentId] DESC;
END;
GO

-- 5.5 Export Compensation
CREATE OR ALTER PROCEDURE [dbo].[usp_Export_GetCompensation]
    @Month TINYINT,
    @Year SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.[CompensationId],
        udb.[FullName] AS DeliveryBoyName,
        udb.[Phone],
        r.[RouteName],
        RIGHT('0' + CAST(c.[Month] AS VARCHAR(2)), 2) + '-' + CAST(c.[Year] AS VARCHAR(4)) AS MonthYear,
        c.[CompensationType],
        c.[EligibleCompletedLitres] AS TotalDeliveredLitres,
        CASE WHEN c.[CompensationType] = 'Fixed Salary' THEN c.[FixedSalaryAmount] ELSE c.[CalculatedCommission] END AS GrossRemuneration,
        c.[Adjustments],
        c.[NetPayableAmount] AS NetPayable,
        c.[Status],
        c.[PaymentReference],
        c.[PaidAt]
    FROM [dbo].[DeliveryBoyMonthlyCompensations] c
    JOIN [dbo].[DeliveryBoys] db ON c.[DeliveryBoyId] = db.[DeliveryBoyId]
    JOIN [dbo].[Users] udb ON db.[UserId] = udb.[UserId]
    LEFT JOIN [dbo].[Routes] r ON db.[PrimaryRouteId] = r.[RouteId]
    WHERE c.[Month] = @Month AND c.[Year] = @Year
    ORDER BY r.[RouteName] ASC, udb.[FullName] ASC;
END;
GO

-- ====================================================================================
-- 6. SETTINGS STORED PROCEDURES
-- ====================================================================================

-- 6.1 Get All Settings
CREATE OR ALTER PROCEDURE [dbo].[usp_AppSettings_GetAll]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [SettingKey],
        [SettingValue],
        [DataType],
        [Description],
        [UpdatedAt]
    FROM [dbo].[AppSettings];
END;
GO

-- 6.2 Update / Insert Setting with Audit Trail
CREATE OR ALTER PROCEDURE [dbo].[usp_AppSettings_Update]
    @SettingKey VARCHAR(50),
    @SettingValue NVARCHAR(500),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OldValue NVARCHAR(500) = NULL;

    SELECT @OldValue = [SettingValue]
    FROM [dbo].[AppSettings]
    WHERE [SettingKey] = @SettingKey;

    BEGIN TRANSACTION;

    IF @OldValue IS NOT NULL
    BEGIN
        UPDATE [dbo].[AppSettings]
        SET [SettingValue] = @SettingValue,
            [UpdatedAt] = SYSUTCDATETIME(),
            [UpdatedBy] = @AdminUserId
        WHERE [SettingKey] = @SettingKey;

        -- Record in AuditLogs
        INSERT INTO [dbo].[AuditLogs] (
            [UserId], [Action], [TableName], [RecordId], 
            [OldValuesJson], [NewValuesJson], [Timestamp]
        ) VALUES (
            @AdminUserId, 'UPDATE_SETTING', 'AppSettings', @SettingKey,
            N'{"' + @SettingKey + N'":"' + REPLACE(@OldValue, '"', '\"') + N'"}',
            N'{"' + @SettingKey + N'":"' + REPLACE(@SettingValue, '"', '\"') + N'"}',
            SYSUTCDATETIME()
        );
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[AppSettings] ([SettingKey], [SettingValue], [DataType], [UpdatedAt], [UpdatedBy])
        VALUES (@SettingKey, @SettingValue, 'String', SYSUTCDATETIME(), @AdminUserId);

        -- Record in AuditLogs
        INSERT INTO [dbo].[AuditLogs] (
            [UserId], [Action], [TableName], [RecordId], 
            [OldValuesJson], [NewValuesJson], [Timestamp]
        ) VALUES (
            @AdminUserId, 'INSERT_SETTING', 'AppSettings', @SettingKey,
            NULL,
            N'{"' + @SettingKey + N'":"' + REPLACE(@SettingValue, '"', '\"') + N'"}',
            SYSUTCDATETIME()
        );
    END;

    COMMIT TRANSACTION;
END;
GO

-- ====================================================================================
-- 7. DELIVERY BOY CUSTOMER ROSTER STORED PROCEDURE
-- ====================================================================================

-- 7.1 Delivery Boy Roster: Returns assigned customers with subscriptions and vacation status
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_GetCustomerRoster]
    @DeliveryBoyId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS DATE);

    SELECT 
        c.[CustomerId],
        c.[CustomerCode],
        c.[FullName],
        c.[Phone],
        c.[Address],
        c.[Area],
        c.[Landmark],
        c.[DeliverySequence],
        cs.[SubscriptionId],
        mc.[CategoryName],
        mc.[UnitOfMeasure] AS Unit,
        cs.[PackagingQuantity] AS DailyQuantity,
        cs.[Shift] AS ShiftPreference,
        CASE WHEN EXISTS (
            SELECT 1 FROM [dbo].[CustomerVacations] v 
            WHERE v.[CustomerId] = c.[CustomerId] 
              AND v.[Status] = 'Approved' 
              AND @Today BETWEEN v.[StartDate] AND v.[EndDate]
        ) THEN 1 ELSE 0 END AS IsOnVacationToday
    FROM [dbo].[Customers] c
    LEFT JOIN [dbo].[CustomerSubscriptions] cs ON c.[CustomerId] = cs.[CustomerId] AND cs.[IsActive] = 1
    LEFT JOIN [dbo].[MilkCategories] mc ON cs.[CategoryId] = mc.[CategoryId]
    WHERE c.[CurrentDeliveryBoyId] = @DeliveryBoyId AND c.[IsActive] = 1
    ORDER BY c.[DeliverySequence] ASC, c.[FullName] ASC;
END;
GO

-- ====================================================================================
-- 8. SECURITY & LEAST-PRIVILEGE ROLE PERMISSIONS
-- ====================================================================================

PRINT 'Configuring database security and procedure execution grants for db_dms_app...';

-- Dashboard
GRANT EXECUTE ON [dbo].[usp_Dashboard_GetAdminMetrics] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Dashboard_GetDeliveryBoyMetrics] TO [db_dms_app];

-- Notifications
GRANT EXECUTE ON [dbo].[usp_Notifications_Create] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_EmitBillPublished] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_ReconcilePublishedBills] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_GetForUser] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_Acknowledge] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_GetUnacknowledgedBillPopups] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_MarkPopupSeen] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Notifications_GetAdminOverview] TO [db_dms_app];

-- Reports
GRANT EXECUTE ON [dbo].[usp_Reports_GetMonthlyBillingVsCollections] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Reports_GetRouteMilkConsumption] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Reports_GetDailyCategoryShiftConsumption] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Reports_GetCustomerBalanceAging] TO [db_dms_app];

-- Exports
GRANT EXECUTE ON [dbo].[usp_Export_GetCustomers] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Export_GetDailyDeliveries] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Export_GetMonthlyBills] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Export_GetPayments] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Export_GetCompensation] TO [db_dms_app];

-- Settings
GRANT EXECUTE ON [dbo].[usp_AppSettings_GetAll] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_AppSettings_Update] TO [db_dms_app];

-- Delivery Boy Roster
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_GetCustomerRoster] TO [db_dms_app];

-- Least-Privilege Security: NotificationPopupSeen table access is strictly encapsulated via stored procedures
-- (usp_Notifications_MarkPopupSeen, usp_Notifications_GetUnacknowledgedBillPopups, usp_Notifications_Acknowledge)
-- benefiting from SQL Server Ownership Chaining. Direct DML is denied.
REVOKE SELECT, INSERT, UPDATE, DELETE ON [dbo].[NotificationPopupSeen] FROM [db_dms_app];
DENY SELECT, INSERT, UPDATE, DELETE ON [dbo].[NotificationPopupSeen] TO [db_dms_app];

PRINT '====================================================================================';
PRINT 'Day 7 Final System Integration database script deployed successfully.';
PRINT '====================================================================================';
GO
