-- ====================================================================================
-- Script 04: Unique Constraints and Performance Indexing Strategy
-- Project: Dairy Management System (DMS)
-- Database: DairyManagementDb
-- Section: 8. Constraints and Indexing Strategy (8.1 & 8.4)
-- Author: Senior SQL Server Database Architect & ADO.NET Backend Engineer
-- Description: Creates unique constraints, filtered unique indexes for active historical
--              intervals, and non-clustered performance indexes. Repeatable and safe.
-- ====================================================================================

USE [DairyManagementDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ====================================================================================
-- 8.1 Primary & Unique Key Constraints / Filtered Unique Indexes
-- ====================================================================================

-- 1. Users: Username uniqueness
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Users_Username' AND object_id = OBJECT_ID(N'[dbo].[Users]'))
BEGIN
    PRINT 'Creating index UQ_Users_Username...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Users_Username]
    ON [dbo].[Users]([Username]);
END
GO

-- 2. Users: Mobile Number Uniqueness (Primary login key for Delivery Boys & mandatory contact)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Users_Phone' AND object_id = OBJECT_ID(N'[dbo].[Users]'))
BEGIN
    PRINT 'Creating index UQ_Users_Phone...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Users_Phone]
    ON [dbo].[Users]([Phone]);
END
GO

-- 3. CustomerDeliveryBoyAssignments: Active Assignment Uniqueness (At most one active delivery boy per customer)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_ActiveCustomerAssignment' AND object_id = OBJECT_ID(N'[dbo].[CustomerDeliveryBoyAssignments]'))
BEGIN
    PRINT 'Creating index UQ_ActiveCustomerAssignment...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_ActiveCustomerAssignment]
    ON [dbo].[CustomerDeliveryBoyAssignments]([CustomerId])
    WHERE [IsActive] = 1 AND [AssignedTo] IS NULL;
END
GO

-- 4. DeliveryBoyCommissionRates: Active Commission Rate Uniqueness (At most one open-ended rate per delivery boy)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_ActiveDeliveryBoyCommissionRate' AND object_id = OBJECT_ID(N'[dbo].[DeliveryBoyCommissionRates]'))
BEGIN
    PRINT 'Creating index UQ_ActiveDeliveryBoyCommissionRate...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_ActiveDeliveryBoyCommissionRate]
    ON [dbo].[DeliveryBoyCommissionRates]([DeliveryBoyId])
    WHERE [EffectiveTo] IS NULL;
END
GO

-- 5. CustomerSubscriptions: Active Customer Subscription Line Uniqueness
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_ActiveCustomerSubscription' AND object_id = OBJECT_ID(N'[dbo].[CustomerSubscriptions]'))
BEGIN
    PRINT 'Creating index UQ_ActiveCustomerSubscription...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_ActiveCustomerSubscription]
    ON [dbo].[CustomerSubscriptions]([CustomerId], [CategoryId], [Shift])
    WHERE [IsActive] = 1 AND [EffectiveTo] IS NULL;
END
GO

-- 6. CustomerCustomPricing: Active Customer Custom Pricing Uniqueness
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_ActiveCustomerCustomPricing' AND object_id = OBJECT_ID(N'[dbo].[CustomerCustomPricing]'))
BEGIN
    PRINT 'Creating index UQ_ActiveCustomerCustomPricing...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_ActiveCustomerCustomPricing]
    ON [dbo].[CustomerCustomPricing]([CustomerId], [CategoryId])
    WHERE [IsActive] = 1 AND [EffectiveTo] IS NULL;
END
GO

-- 7. DailyMilkDeliveries: Daily Entry Uniqueness (Duplicate Prevention per customer, date & shift)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_DailyDelivery_Customer_Date_Shift' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]'))
BEGIN
    PRINT 'Creating index UQ_DailyDelivery_Customer_Date_Shift...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_DailyDelivery_Customer_Date_Shift]
    ON [dbo].[DailyMilkDeliveries]([CustomerId], [EntryDate], [Shift]);
END
GO

-- 8. DailyMilkDeliveryItems: Daily Line Item Uniqueness per delivery and category
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_DailyItem_Delivery_Category' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveryItems]'))
BEGIN
    PRINT 'Creating index UQ_DailyItem_Delivery_Category...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_DailyItem_Delivery_Category]
    ON [dbo].[DailyMilkDeliveryItems]([DeliveryId], [CategoryId]);
END
GO

-- 9. MonthlyBills: Calendar-Month Bill Uniqueness per customer
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Customer_MonthlyBill' AND object_id = OBJECT_ID(N'[dbo].[MonthlyBills]'))
BEGIN
    PRINT 'Creating index UQ_Customer_MonthlyBill...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Customer_MonthlyBill]
    ON [dbo].[MonthlyBills]([CustomerId], [BillingMonth], [BillingYear]);
END
GO

-- 10. MonthlyBillItems: Bill Line Item Configuration Uniqueness (Preventing Duplicate Lines per Configuration)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_BillItem_Bill_ConfigSignature' AND object_id = OBJECT_ID(N'[dbo].[MonthlyBillItems]'))
BEGIN
    PRINT 'Creating index UQ_BillItem_Bill_ConfigSignature...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_BillItem_Bill_ConfigSignature]
    ON [dbo].[MonthlyBillItems](
        [BillId],
        [CategoryId],
        [ProductTypeSnapshot],
        [UnitOfMeasureSnapshot],
        [PricingBasisSnapshot],
        [BillingUnitEquivalentSnapshot],
        [LitreEquivalentSnapshot],
        [UnitPriceSnapshot]
    );
END
GO

-- 11. PaymentAllocations: Unique allocation link between a Payment and a Bill
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Payment_Bill_Allocation' AND object_id = OBJECT_ID(N'[dbo].[PaymentAllocations]'))
BEGIN
    PRINT 'Creating index UQ_Payment_Bill_Allocation...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Payment_Bill_Allocation]
    ON [dbo].[PaymentAllocations]([PaymentId], [BillId]);
END
GO

-- 12. DeliveryBoyMonthlyCompensations: Monthly Compensation Uniqueness per agent, month & year
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_DeliveryBoy_Month_Compensation' AND object_id = OBJECT_ID(N'[dbo].[DeliveryBoyMonthlyCompensations]'))
BEGIN
    PRINT 'Creating index UQ_DeliveryBoy_Month_Compensation...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_DeliveryBoy_Month_Compensation]
    ON [dbo].[DeliveryBoyMonthlyCompensations]([DeliveryBoyId], [Month], [Year]);
END
GO

-- 13. NotificationAcknowledgements: Uniqueness of user acknowledgement per notification
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Notification_User_Ack' AND object_id = OBJECT_ID(N'[dbo].[NotificationAcknowledgements]'))
BEGIN
    PRINT 'Creating index UQ_Notification_User_Ack...';
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_Notification_User_Ack]
    ON [dbo].[NotificationAcknowledgements]([NotificationId], [UserId]);
END
GO


-- ====================================================================================
-- 8.4 Performance Indexing Recommendations (Non-Clustered)
-- ====================================================================================

-- 1. Daily Entry Roster Generation Index (Speeding up shift entry sheets)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyDeliveries_Date_Shift_Route' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]'))
BEGIN
    PRINT 'Creating performance index IX_DailyDeliveries_Date_Shift_Route...';
    CREATE NONCLUSTERED INDEX [IX_DailyDeliveries_Date_Shift_Route]
    ON [dbo].[DailyMilkDeliveries]([EntryDate], [Shift], [DeliveryBoyId])
    INCLUDE ([CustomerId], [Status], [TotalLiquidVolumeLitres], [TotalAmount], [EligibleLitreQuantity]);
END
GO

-- 2. Customer Route Sequence Index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_Route_Sequence' AND object_id = OBJECT_ID(N'[dbo].[Customers]'))
BEGIN
    PRINT 'Creating performance index IX_Customers_Route_Sequence...';
    CREATE NONCLUSTERED INDEX [IX_Customers_Route_Sequence]
    ON [dbo].[Customers]([RouteId], [DeliverySequence])
    INCLUDE ([CustomerId], [FullName], [Address], [PreferredShift], [Status])
    WHERE [IsActive] = 1;
END
GO

-- 3. Vacation Gatekeeper Index (Live check during entry roster generation)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerVacations_ActiveLookup' AND object_id = OBJECT_ID(N'[dbo].[CustomerVacations]'))
BEGIN
    PRINT 'Creating performance index IX_CustomerVacations_ActiveLookup...';
    CREATE NONCLUSTERED INDEX [IX_CustomerVacations_ActiveLookup]
    ON [dbo].[CustomerVacations]([CustomerId], [StartDate], [EndDate], [Status])
    INCLUDE ([Shift], [ResumedDate])
    WHERE [Status] = 'Approved';
END
GO

-- 4. Customer Pricing Effective-Date Lookup Index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerCustomPricing_Effective' AND object_id = OBJECT_ID(N'[dbo].[CustomerCustomPricing]'))
BEGIN
    PRINT 'Creating performance index IX_CustomerCustomPricing_Effective...';
    CREATE NONCLUSTERED INDEX [IX_CustomerCustomPricing_Effective]
    ON [dbo].[CustomerCustomPricing]([CustomerId], [CategoryId], [EffectiveFrom], [EffectiveTo])
    INCLUDE ([CustomPrice])
    WHERE [IsActive] = 1;
END
GO

-- 5. Outstanding Bills Lookup Index (For Manual Payment Allocation Modal)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MonthlyBills_Outstanding' AND object_id = OBJECT_ID(N'[dbo].[MonthlyBills]'))
BEGIN
    PRINT 'Creating performance index IX_MonthlyBills_Outstanding...';
    CREATE NONCLUSTERED INDEX [IX_MonthlyBills_Outstanding]
    ON [dbo].[MonthlyBills]([CustomerId], [BalanceAmount])
    INCLUDE ([BillNumber], [BillingMonth], [BillingYear], [CurrentMonthInvoiceAmount], [PaidAmount], [PaymentStatus])
    WHERE [BalanceAmount] > 0;
END
GO

-- 6. Commission Litres & Rate Snapshot Aggregation Index (For Monthly Payroll Runs)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DailyDeliveries_CommissionAggregation' AND object_id = OBJECT_ID(N'[dbo].[DailyMilkDeliveries]'))
BEGIN
    PRINT 'Creating performance index IX_DailyDeliveries_CommissionAggregation...';
    CREATE NONCLUSTERED INDEX [IX_DailyDeliveries_CommissionAggregation]
    ON [dbo].[DailyMilkDeliveries]([DeliveryBoyId], [EntryDate], [Status])
    INCLUDE ([EligibleLitreQuantity], [CommissionRateSnapshot])
    WHERE [Status] = 'Completed';
END
GO

-- 7. Unread Notification Query Index (For Delivery Boy Topbar/Panel)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Target' AND object_id = OBJECT_ID(N'[dbo].[Notifications]'))
BEGIN
    PRINT 'Creating performance index IX_Notifications_Target...';
    CREATE NONCLUSTERED INDEX [IX_Notifications_Target]
    ON [dbo].[Notifications]([TargetRole], [TargetDeliveryBoyId], [TargetRouteId], [CreatedAt])
    INCLUDE ([Title], [Message], [NotificationType]);
END
GO

PRINT 'All indexes created or verified successfully.';
GO
