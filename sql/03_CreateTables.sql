-- ====================================================================================
-- Script 03: Database Table Creation (All 22 Approved Tables)
-- Project: Dairy Management System (DMS)
-- Database: DairyManagementDb
-- Section: 5. Detailed Table Specifications (5.1 - 5.11)
-- Author: Senior SQL Server Database Architect & ADO.NET Backend Engineer
-- Description: Creates all 22 relational tables with exact primary keys, foreign keys,
--              defaults, and CHECK constraints. Repeatable and safe to execute.
-- Cascade Deletion Policy: Exactly three owned child tables use ON DELETE CASCADE:
--              1. DailyMilkDeliveryItems (child of DailyMilkDeliveries)
--              2. MonthlyBillItems (child of MonthlyBills)
--              3. NotificationAcknowledgements (child of Notifications)
--              All other foreign keys enforce ON DELETE NO ACTION (restrict).
-- ====================================================================================

USE [DairyManagementDb];
GO

-- 1. Users (Authentication & Identity for Admin & Delivery Boys)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Users')
BEGIN
    PRINT 'Creating table Users...';
    CREATE TABLE [dbo].[Users] (
        [UserId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED,
        [Username] NVARCHAR(50) NOT NULL,
        [PasswordHash] NVARCHAR(256) NOT NULL,
        [Role] NVARCHAR(20) NOT NULL CONSTRAINT [CK_Users_Role] CHECK ([Role] IN ('Admin', 'DeliveryBoy')),
        [FullName] NVARCHAR(100) NOT NULL,
        [Phone] VARCHAR(15) NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_Users_IsActive] DEFAULT (1),
        [LastLoginAt] DATETIME2(7) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Users_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NULL,
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL
    );

    ALTER TABLE [dbo].[Users]
    ADD CONSTRAINT [FK_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [FK_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([UserId]);
END
GO

-- 2. Routes (Geographical sectors)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Routes')
BEGIN
    PRINT 'Creating table Routes...';
    CREATE TABLE [dbo].[Routes] (
        [RouteId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Routes] PRIMARY KEY CLUSTERED,
        [RouteCode] NVARCHAR(20) NOT NULL CONSTRAINT [UQ_Routes_RouteCode] UNIQUE,
        [RouteName] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(250) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_Routes_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Routes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_Routes_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_Routes_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 3. DeliveryBoys (Delivery personnel profiles & compensation basis)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DeliveryBoys')
BEGIN
    PRINT 'Creating table DeliveryBoys...';
    CREATE TABLE [dbo].[DeliveryBoys] (
        [DeliveryBoyId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DeliveryBoys] PRIMARY KEY CLUSTERED,
        [UserId] INT NOT NULL CONSTRAINT [UQ_DeliveryBoys_UserId] UNIQUE CONSTRAINT [FK_DeliveryBoys_UserId] REFERENCES [dbo].[Users]([UserId]),
        [PrimaryRouteId] INT NULL CONSTRAINT [FK_DeliveryBoys_PrimaryRouteId] REFERENCES [dbo].[Routes]([RouteId]),
        [CompensationType] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DeliveryBoys_Compensation] CHECK ([CompensationType] IN ('Fixed Salary', 'Commission')),
        [CurrentCommissionRate] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoys_CurrentCommissionRate] DEFAULT (0.00) CONSTRAINT [CK_DeliveryBoys_CurrentCommissionRate] CHECK ([CurrentCommissionRate] >= 0),
        [MonthlyBaseSalary] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoys_MonthlyBaseSalary] DEFAULT (0.00) CONSTRAINT [CK_DeliveryBoys_MonthlyBaseSalary] CHECK ([MonthlyBaseSalary] >= 0),
        [VehicleNumber] NVARCHAR(30) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_DeliveryBoys_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DeliveryBoys_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DeliveryBoys_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_DeliveryBoys_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 4. DeliveryBoyCommissionRates (Historical per-litre commission rates)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DeliveryBoyCommissionRates')
BEGIN
    PRINT 'Creating table DeliveryBoyCommissionRates...';
    CREATE TABLE [dbo].[DeliveryBoyCommissionRates] (
        [RateId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DeliveryBoyCommissionRates] PRIMARY KEY CLUSTERED,
        [DeliveryBoyId] INT NOT NULL CONSTRAINT [FK_DeliveryBoyCommissionRates_DeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [CommissionRatePerLitre] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_DeliveryBoyCommissionRates_Rate] CHECK ([CommissionRatePerLitre] >= 0),
        [EffectiveFrom] DATE NOT NULL,
        [EffectiveTo] DATE NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DeliveryBoyCommissionRates_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DeliveryBoyCommissionRates_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_DeliveryBoyCommissionRates_Dates] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom])
    );
END
GO

-- 5. Customers (Customer master record with Gujarati support and route link)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Customers')
BEGIN
    PRINT 'Creating table Customers...';
    CREATE TABLE [dbo].[Customers] (
        [CustomerId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Customers] PRIMARY KEY CLUSTERED,
        [CustomerCode] VARCHAR(20) NOT NULL CONSTRAINT [UQ_Customers_CustomerCode] UNIQUE,
        [FullName] NVARCHAR(100) NOT NULL,
        [Phone] VARCHAR(15) NOT NULL,
        [AlternatePhone] VARCHAR(15) NULL,
        [Address] NVARCHAR(250) NOT NULL,
        [Landmark] NVARCHAR(100) NULL,
        [RouteId] INT NOT NULL CONSTRAINT [FK_Customers_RouteId] REFERENCES [dbo].[Routes]([RouteId]),
        [CurrentDeliveryBoyId] INT NULL CONSTRAINT [FK_Customers_CurrentDeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [DeliverySequence] INT NOT NULL CONSTRAINT [DF_Customers_DeliverySequence] DEFAULT (0),
        [PreferredShift] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Customers_PreferredShift] DEFAULT ('Both') CONSTRAINT [CK_Customers_PreferredShift] CHECK ([PreferredShift] IN ('Morning', 'Evening', 'Both')),
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Customers_Status] DEFAULT ('Active') CONSTRAINT [CK_Customers_Status] CHECK ([Status] IN ('Active', 'Suspended', 'Inactive')),
        [IsActive] BIT NOT NULL CONSTRAINT [DF_Customers_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Customers_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_Customers_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_Customers_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 6. CustomerDeliveryBoyAssignments (Historical customer-to-delivery-boy route mapping)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerDeliveryBoyAssignments')
BEGIN
    PRINT 'Creating table CustomerDeliveryBoyAssignments...';
    CREATE TABLE [dbo].[CustomerDeliveryBoyAssignments] (
        [AssignmentId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerDeliveryBoyAssignments] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_CustomerAssignments_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [DeliveryBoyId] INT NOT NULL CONSTRAINT [FK_CustomerAssignments_DeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [AssignedFrom] DATE NOT NULL,
        [AssignedTo] DATE NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_CustomerAssignments_IsActive] DEFAULT (1),
        [Notes] NVARCHAR(250) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerAssignments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_CustomerAssignments_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_CustomerAssignments_UpdatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_CustomerDeliveryBoyAssignments_Dates] CHECK ([AssignedTo] IS NULL OR [AssignedTo] >= [AssignedFrom])
    );
END
GO

-- 7. MilkCategories (Dynamic dairy catalog: liquid & non-liquid, pricing bases)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MilkCategories')
BEGIN
    PRINT 'Creating table MilkCategories...';
    CREATE TABLE [dbo].[MilkCategories] (
        [CategoryId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MilkCategories] PRIMARY KEY CLUSTERED,
        [CategoryCode] NVARCHAR(20) NOT NULL CONSTRAINT [UQ_MilkCategories_CategoryCode] UNIQUE,
        [CategoryName] NVARCHAR(100) NOT NULL,
        [ProductType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_MilkCategories_ProductType] DEFAULT ('Liquid') CONSTRAINT [CK_MilkCategories_ProductType] CHECK ([ProductType] IN ('Liquid', 'Non-Liquid')),
        [UnitOfMeasure] NVARCHAR(20) NOT NULL CONSTRAINT [DF_MilkCategories_UnitOfMeasure] DEFAULT ('Litre'),
        [PricingBasis] NVARCHAR(20) NOT NULL CONSTRAINT [DF_MilkCategories_PricingBasis] DEFAULT ('Per Litre') CONSTRAINT [CK_MilkCategories_PricingBasis] CHECK ([PricingBasis] IN ('Per Litre', 'Per Kg', 'Per Unit')),
        [BillingUnitEquivalent] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_MilkCategories_BillingUnitEq] DEFAULT (1.000) CONSTRAINT [CK_MilkCategories_BillingUnitEq] CHECK ([BillingUnitEquivalent] >= 0),
        [LitreEquivalent] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_MilkCategories_LitreEq] DEFAULT (1.000) CONSTRAINT [CK_MilkCategories_LitreEquivalent] CHECK ([LitreEquivalent] >= 0),
        [DefaultPrice] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_MilkCategories_DefaultPrice] CHECK ([DefaultPrice] > 0),
        [DisplayOrder] INT NOT NULL CONSTRAINT [DF_MilkCategories_DisplayOrder] DEFAULT (0),
        [IsActive] BIT NOT NULL CONSTRAINT [DF_MilkCategories_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_MilkCategories_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_MilkCategories_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_MilkCategories_UpdatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_MilkCategories_NonLiquid_LitreEq] CHECK (
            ([ProductType] = 'Non-Liquid' AND [LitreEquivalent] = 0.000) OR
            ([ProductType] = 'Liquid' AND [LitreEquivalent] > 0.000)
        )
    );
END
GO

-- 8. CustomerSubscriptions (Multi-category daily subscription lines per customer & shift)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerSubscriptions')
BEGIN
    PRINT 'Creating table CustomerSubscriptions...';
    CREATE TABLE [dbo].[CustomerSubscriptions] (
        [SubscriptionId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerSubscriptions] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_CustomerSubscriptions_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [CategoryId] INT NOT NULL CONSTRAINT [FK_CustomerSubscriptions_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [Shift] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerSubscriptions_Shift] DEFAULT ('Both') CONSTRAINT [CK_CustomerSubscriptions_Shift] CHECK ([Shift] IN ('Morning', 'Evening', 'Both')),
        [PackagingQuantity] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_CustomerSubscriptions_Quantity] CHECK ([PackagingQuantity] > 0),
        [DailyLiquidVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_CustomerSubscriptions_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_CustomerSubscriptions_LiquidVolume] CHECK ([DailyLiquidVolumeLitres] >= 0),
        [EffectiveFrom] DATE NOT NULL,
        [EffectiveTo] DATE NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_CustomerSubscriptions_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerSubscriptions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_CustomerSubscriptions_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_CustomerSubscriptions_UpdatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_CustomerSubscriptions_Dates] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom])
    );
END
GO

-- 9. CustomerCustomPricing (Customer-specific unit price overrides with effective date ranges)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerCustomPricing')
BEGIN
    PRINT 'Creating table CustomerCustomPricing...';
    CREATE TABLE [dbo].[CustomerCustomPricing] (
        [PricingId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerCustomPricing] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_CustomerCustomPricing_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [CategoryId] INT NOT NULL CONSTRAINT [FK_CustomerCustomPricing_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [CustomPrice] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_CustomerCustomPricing_Price] CHECK ([CustomPrice] > 0),
        [EffectiveFrom] DATE NOT NULL,
        [EffectiveTo] DATE NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_CustomerCustomPricing_IsActive] DEFAULT (1),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerCustomPricing_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_CustomerCustomPricing_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_CustomerCustomPricing_Dates] CHECK ([EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom])
    );
END
GO

-- 10. CustomerVacations (Customer non-delivery requests with approval lifecycle)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomerVacations')
BEGIN
    PRINT 'Creating table CustomerVacations...';
    CREATE TABLE [dbo].[CustomerVacations] (
        [VacationId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CustomerVacations] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_CustomerVacations_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [ReportedByDeliveryBoyId] INT NULL CONSTRAINT [FK_CustomerVacations_ReportedByDeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [StartDate] DATE NOT NULL,
        [EndDate] DATE NOT NULL,
        [ResumedDate] DATE NULL,
        [Shift] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerVacations_Shift] DEFAULT ('Both') CONSTRAINT [CK_CustomerVacations_Shift] CHECK ([Shift] IN ('Morning', 'Evening', 'Both')),
        [Reason] NVARCHAR(250) NULL,
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerVacations_Status] DEFAULT ('Pending') CONSTRAINT [CK_CustomerVacations_Status] CHECK ([Status] IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
        [ApprovedBy] INT NULL CONSTRAINT [FK_CustomerVacations_ApprovedBy] REFERENCES [dbo].[Users]([UserId]),
        [ApprovedAt] DATETIME2(7) NULL,
        [RejectionReason] NVARCHAR(250) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerVacations_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_CustomerVacations_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_CustomerVacations_UpdatedBy] REFERENCES [dbo].[Users]([UserId]),
        CONSTRAINT [CK_CustomerVacations_Dates] CHECK ([EndDate] >= [StartDate])
    );
END
GO

-- 11. DailyMilkDeliveries (Daily shift delivery header per customer, date & shift)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DailyMilkDeliveries')
BEGIN
    PRINT 'Creating table DailyMilkDeliveries...';
    CREATE TABLE [dbo].[DailyMilkDeliveries] (
        [DeliveryId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DailyMilkDeliveries] PRIMARY KEY CLUSTERED,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_DailyMilkDeliveries_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [DeliveryBoyId] INT NOT NULL CONSTRAINT [FK_DailyMilkDeliveries_DeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [EntryDate] DATE NOT NULL,
        [Shift] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DailyMilkDeliveries_Shift] CHECK ([Shift] IN ('Morning', 'Evening')),
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_DailyMilkDeliveries_Status] DEFAULT ('Pending') CONSTRAINT [CK_DailyMilkDeliveries_Status] CHECK ([Status] IN ('Completed', 'Pending', 'Not Delivered')),
        [VacationId] INT NULL CONSTRAINT [FK_DailyMilkDeliveries_VacationId] REFERENCES [dbo].[CustomerVacations]([VacationId]),
        [NonDeliveryReason] NVARCHAR(250) NULL,
        [CommissionRateSnapshot] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DailyDeliveries_CommissionSnapshot] DEFAULT (0.00) CONSTRAINT [CK_DailyDeliveries_CommissionSnapshot] CHECK ([CommissionRateSnapshot] >= 0),
        [TotalLiquidVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_DailyDeliveries_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_DailyDeliveries_LiquidVolume] CHECK ([TotalLiquidVolumeLitres] >= 0),
        [EligibleLitreQuantity] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_DailyDeliveries_EligibleLitres] DEFAULT (0.000) CONSTRAINT [CK_DailyDeliveries_EligibleLitres] CHECK ([EligibleLitreQuantity] >= 0),
        [TotalAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DailyDeliveries_TotalAmount] DEFAULT (0.00) CONSTRAINT [CK_DailyDeliveries_TotalAmount] CHECK ([TotalAmount] >= 0),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DailyMilkDeliveries_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DailyMilkDeliveries_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_DailyMilkDeliveries_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 12. DailyMilkDeliveryItems (Category breakdown: packaging quantity, snapshots & subtotal)
-- Allowed CASCADE DELETE from DailyMilkDeliveries as owned composition child during draft entry
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DailyMilkDeliveryItems')
BEGIN
    PRINT 'Creating table DailyMilkDeliveryItems...';
    CREATE TABLE [dbo].[DailyMilkDeliveryItems] (
        [DeliveryItemId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DailyMilkDeliveryItems] PRIMARY KEY CLUSTERED,
        [DeliveryId] BIGINT NOT NULL CONSTRAINT [FK_DailyDeliveryItems_DeliveryId] REFERENCES [dbo].[DailyMilkDeliveries]([DeliveryId]) ON DELETE CASCADE,
        [CategoryId] INT NOT NULL CONSTRAINT [FK_DailyDeliveryItems_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [PackagingQuantity] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_PackagingQuantity] CHECK ([PackagingQuantity] >= 0),
        [ProductTypeSnapshot] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_ProductType] CHECK ([ProductTypeSnapshot] IN ('Liquid', 'Non-Liquid')),
        [UnitOfMeasureSnapshot] NVARCHAR(20) NOT NULL,
        [PricingBasisSnapshot] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_PricingBasis] CHECK ([PricingBasisSnapshot] IN ('Per Litre', 'Per Kg', 'Per Unit')),
        [BillingUnitEquivalentSnapshot] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_BillingUnitEq] CHECK ([BillingUnitEquivalentSnapshot] >= 0),
        [LitreEquivalentSnapshot] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_LitreEq] CHECK ([LitreEquivalentSnapshot] >= 0),
        [LiquidVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_DailyDeliveryItems_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_DailyDeliveryItems_LiquidVolume] CHECK ([LiquidVolumeLitres] >= 0),
        [UnitPriceSnapshot] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_UnitPrice] CHECK ([UnitPriceSnapshot] >= 0),
        [SubTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_DailyDeliveryItems_SubTotal] CHECK ([SubTotal] >= 0),
        CONSTRAINT [CK_DailyDeliveryItems_NonLiquid_LitreEq] CHECK (
            ([ProductTypeSnapshot] = 'Non-Liquid' AND [LitreEquivalentSnapshot] = 0.000) OR
            ([ProductTypeSnapshot] = 'Liquid' AND [LitreEquivalentSnapshot] > 0.000)
        )
    );
END
GO

-- 13. MonthlyBills (Calendar-month invoices: liquid milk volume, financial totals & open-item dues)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MonthlyBills')
BEGIN
    PRINT 'Creating table MonthlyBills...';
    CREATE TABLE [dbo].[MonthlyBills] (
        [BillId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MonthlyBills] PRIMARY KEY CLUSTERED,
        [BillNumber] NVARCHAR(30) NOT NULL CONSTRAINT [UQ_MonthlyBills_BillNumber] UNIQUE,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_MonthlyBills_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [BillingMonth] TINYINT NOT NULL CONSTRAINT [CK_MonthlyBills_BillingMonth] CHECK ([BillingMonth] BETWEEN 1 AND 12),
        [BillingYear] SMALLINT NOT NULL CONSTRAINT [CK_MonthlyBills_BillingYear] CHECK ([BillingYear] BETWEEN 2000 AND 2100),
        [PeriodStartDate] DATE NOT NULL,
        [PeriodEndDate] DATE NOT NULL,
        [TotalLiquidMilkVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_MonthlyBills_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_MonthlyBills_LiquidVolume] CHECK ([TotalLiquidMilkVolumeLitres] >= 0),
        [CurrentMonthMilkAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_MonthlyBills_MilkAmount] DEFAULT (0.00) CONSTRAINT [CK_MonthlyBills_MilkAmount] CHECK ([CurrentMonthMilkAmount] >= 0),
        [Adjustments] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_MonthlyBills_Adjustments] DEFAULT (0.00),
        [CurrentMonthInvoiceAmount] DECIMAL(18,2) NOT NULL,
        [PreviousBalanceSnapshot] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_MonthlyBills_PrevBalance] DEFAULT (0.00),
        [TotalStatementAmount] DECIMAL(18,2) NOT NULL,
        [PaidAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_MonthlyBills_PaidAmount] DEFAULT (0.00) CONSTRAINT [CK_MonthlyBills_PaidAmount] CHECK ([PaidAmount] >= 0),
        [BalanceAmount] DECIMAL(18,2) NOT NULL,
        [PaymentStatus] NVARCHAR(20) NOT NULL CONSTRAINT [DF_MonthlyBills_PaymentStatus] DEFAULT ('Unpaid') CONSTRAINT [CK_MonthlyBills_PaymentStatus] CHECK ([PaymentStatus] IN ('Unpaid', 'Partially Paid', 'Paid', 'Overdue')),
        [IsPublished] BIT NOT NULL CONSTRAINT [DF_MonthlyBills_IsPublished] DEFAULT (0),
        [PublishedAt] DATETIME2(7) NULL,
        [Version] INT NOT NULL CONSTRAINT [DF_MonthlyBills_Version] DEFAULT (1),
        [RevisionReason] NVARCHAR(250) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_MonthlyBills_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_MonthlyBills_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_MonthlyBills_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 14. MonthlyBillItems (Distinct configuration lines per bill)
-- Allowed CASCADE DELETE from MonthlyBills as owned composition child during draft generation
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MonthlyBillItems')
BEGIN
    PRINT 'Creating table MonthlyBillItems...';
    CREATE TABLE [dbo].[MonthlyBillItems] (
        [BillItemId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MonthlyBillItems] PRIMARY KEY CLUSTERED,
        [BillId] BIGINT NOT NULL CONSTRAINT [FK_MonthlyBillItems_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]) ON DELETE CASCADE,
        [CategoryId] INT NOT NULL CONSTRAINT [FK_MonthlyBillItems_CategoryId] REFERENCES [dbo].[MilkCategories]([CategoryId]),
        [ProductTypeSnapshot] NVARCHAR(20) NOT NULL CONSTRAINT [CK_MonthlyBillItems_ProductType] CHECK ([ProductTypeSnapshot] IN ('Liquid', 'Non-Liquid')),
        [UnitOfMeasureSnapshot] NVARCHAR(20) NOT NULL,
        [PricingBasisSnapshot] NVARCHAR(20) NOT NULL CONSTRAINT [CK_MonthlyBillItems_PricingBasis] CHECK ([PricingBasisSnapshot] IN ('Per Litre', 'Per Kg', 'Per Unit')),
        [BillingUnitEquivalentSnapshot] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_MonthlyBillItems_BillingUnitEq] CHECK ([BillingUnitEquivalentSnapshot] >= 0),
        [LitreEquivalentSnapshot] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_MonthlyBillItems_LitreEq] CHECK ([LitreEquivalentSnapshot] >= 0),
        [UnitPriceSnapshot] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_MonthlyBillItems_UnitPrice] CHECK ([UnitPriceSnapshot] >= 0),
        [DeliveredDaysCount] INT NOT NULL CONSTRAINT [CK_MonthlyBillItems_DeliveredDays] CHECK ([DeliveredDaysCount] >= 0),
        [TotalPackagingQuantity] DECIMAL(18,3) NOT NULL CONSTRAINT [CK_MonthlyBillItems_TotalPackagingQuantity] CHECK ([TotalPackagingQuantity] >= 0),
        [TotalLiquidVolumeLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_MonthlyBillItems_LiquidVolume] DEFAULT (0.000) CONSTRAINT [CK_MonthlyBillItems_TotalLiquidVolume] CHECK ([TotalLiquidVolumeLitres] >= 0),
        [SubTotal] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_MonthlyBillItems_SubTotal] CHECK ([SubTotal] >= 0),
        CONSTRAINT [CK_MonthlyBillItems_NonLiquid_LitreEq] CHECK (
            ([ProductTypeSnapshot] = 'Non-Liquid' AND [LitreEquivalentSnapshot] = 0.000) OR
            ([ProductTypeSnapshot] = 'Liquid' AND [LitreEquivalentSnapshot] > 0.000)
        )
    );
END
GO

-- 15. MonthlyBillRevisions (Immutable historical audit log of bill recalculations)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MonthlyBillRevisions')
BEGIN
    PRINT 'Creating table MonthlyBillRevisions...';
    CREATE TABLE [dbo].[MonthlyBillRevisions] (
        [RevisionId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MonthlyBillRevisions] PRIMARY KEY CLUSTERED,
        [BillId] BIGINT NOT NULL CONSTRAINT [FK_MonthlyBillRevisions_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]),
        [VersionNumber] INT NOT NULL,
        [PriorMilkAmount] DECIMAL(18,2) NOT NULL,
        [NewMilkAmount] DECIMAL(18,2) NOT NULL,
        [PriorAdjustments] DECIMAL(18,2) NOT NULL,
        [NewAdjustments] DECIMAL(18,2) NOT NULL,
        [PriorInvoiceAmount] DECIMAL(18,2) NOT NULL,
        [NewInvoiceAmount] DECIMAL(18,2) NOT NULL,
        [PreviousBalanceSnapshot] DECIMAL(18,2) NOT NULL,
        [PaidAmountSnapshot] DECIMAL(18,2) NOT NULL,
        [PriorBalanceAmount] DECIMAL(18,2) NOT NULL,
        [NewBalanceAmount] DECIMAL(18,2) NOT NULL,
        [Reason] NVARCHAR(250) NOT NULL,
        [RevisedBy] INT NOT NULL CONSTRAINT [FK_MonthlyBillRevisions_RevisedBy] REFERENCES [dbo].[Users]([UserId]),
        [RevisedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_MonthlyBillRevisions_RevisedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

-- 16. Payments (Receipts of funds received: Online / Offline, 0..N allocations)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Payments')
BEGIN
    PRINT 'Creating table Payments...';
    CREATE TABLE [dbo].[Payments] (
        [PaymentId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Payments] PRIMARY KEY CLUSTERED,
        [ReceiptNumber] NVARCHAR(30) NOT NULL CONSTRAINT [UQ_Payments_ReceiptNumber] UNIQUE,
        [CustomerId] INT NOT NULL CONSTRAINT [FK_Payments_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [PaymentDate] DATE NOT NULL,
        [Amount] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_Payments_Amount] CHECK ([Amount] > 0),
        [PaymentMode] NVARCHAR(20) NOT NULL CONSTRAINT [CK_Payments_PaymentMode] CHECK ([PaymentMode] IN ('Online', 'Offline')),
        [ReferenceNumber] NVARCHAR(50) NULL,
        [Notes] NVARCHAR(250) NULL,
        [IsVoided] BIT NOT NULL CONSTRAINT [DF_Payments_IsVoided] DEFAULT (0),
        [VoidedAt] DATETIME2(7) NULL,
        [VoidedBy] INT NULL CONSTRAINT [FK_Payments_VoidedBy] REFERENCES [dbo].[Users]([UserId]),
        [VoidReason] NVARCHAR(250) NULL,
        [ReceivedBy] INT NOT NULL CONSTRAINT [FK_Payments_ReceivedBy] REFERENCES [dbo].[Users]([UserId]),
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Payments_CreatedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

-- 17. PaymentAllocations (Manual month-wise allocation breakdown across monthly bills)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PaymentAllocations')
BEGIN
    PRINT 'Creating table PaymentAllocations...';
    CREATE TABLE [dbo].[PaymentAllocations] (
        [AllocationId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_PaymentAllocations] PRIMARY KEY CLUSTERED,
        [PaymentId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocations_PaymentId] REFERENCES [dbo].[Payments]([PaymentId]),
        [BillId] BIGINT NOT NULL CONSTRAINT [FK_PaymentAllocations_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]),
        [AllocatedAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_PaymentAllocations_Amount] CHECK ([AllocatedAmount] > 0),
        [IsVoided] BIT NOT NULL CONSTRAINT [DF_PaymentAllocations_IsVoided] DEFAULT (0),
        [AllocatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_PaymentAllocations_AllocatedAt] DEFAULT (SYSUTCDATETIME()),
        [AllocatedBy] INT NOT NULL CONSTRAINT [FK_PaymentAllocations_AllocatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 18. DeliveryBoyMonthlyCompensations (Monthly payroll & commission summaries)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DeliveryBoyMonthlyCompensations')
BEGIN
    PRINT 'Creating table DeliveryBoyMonthlyCompensations...';
    CREATE TABLE [dbo].[DeliveryBoyMonthlyCompensations] (
        [CompensationId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DeliveryBoyMonthlyCompensations] PRIMARY KEY CLUSTERED,
        [DeliveryBoyId] INT NOT NULL CONSTRAINT [FK_DeliveryBoyCompensations_DeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [Month] TINYINT NOT NULL CONSTRAINT [CK_DeliveryBoyCompensations_Month] CHECK ([Month] BETWEEN 1 AND 12),
        [Year] SMALLINT NOT NULL CONSTRAINT [CK_DeliveryBoyCompensations_Year] CHECK ([Year] BETWEEN 2000 AND 2100),
        [CompensationType] NVARCHAR(20) NOT NULL CONSTRAINT [CK_DeliveryBoyMonthlyCompensations_Type] CHECK ([CompensationType] IN ('Fixed Salary', 'Commission')),
        [EligibleCompletedLitres] DECIMAL(18,3) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_Litres] DEFAULT (0.000) CONSTRAINT [CK_DeliveryBoyCompensations_Litres] CHECK ([EligibleCompletedLitres] >= 0),
        [ApplicableCommissionRate] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_Rate] DEFAULT (0.00) CONSTRAINT [CK_DeliveryBoyCompensations_Rate] CHECK ([ApplicableCommissionRate] >= 0),
        [CalculatedCommission] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_CalcCommission] DEFAULT (0.00) CONSTRAINT [CK_DeliveryBoyCompensations_CalcCommission] CHECK ([CalculatedCommission] >= 0),
        [FixedSalaryAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_FixedSalary] DEFAULT (0.00) CONSTRAINT [CK_DeliveryBoyCompensations_FixedSalary] CHECK ([FixedSalaryAmount] >= 0),
        [Adjustments] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_Adjustments] DEFAULT (0.00),
        [NetPayableAmount] DECIMAL(18,2) NOT NULL CONSTRAINT [CK_DeliveryBoyCompensations_NetPayable] CHECK ([NetPayableAmount] >= 0),
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_Status] DEFAULT ('Draft') CONSTRAINT [CK_DeliveryBoyMonthlyCompensations_Status] CHECK ([Status] IN ('Draft', 'Finalized', 'Paid')),
        [FinalizedAt] DATETIME2(7) NULL,
        [FinalizedBy] INT NULL CONSTRAINT [FK_DeliveryBoyCompensations_FinalizedBy] REFERENCES [dbo].[Users]([UserId]),
        [PaidAt] DATETIME2(7) NULL,
        [PaymentReference] NVARCHAR(50) NULL,
        [PaidBy] INT NULL CONSTRAINT [FK_DeliveryBoyCompensations_PaidBy] REFERENCES [dbo].[Users]([UserId]),
        [Notes] NVARCHAR(250) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DeliveryBoyCompensations_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DeliveryBoyCompensations_CreatedBy] REFERENCES [dbo].[Users]([UserId]),
        [UpdatedAt] DATETIME2(7) NULL,
        [UpdatedBy] INT NULL CONSTRAINT [FK_DeliveryBoyCompensations_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 19. Notifications (Broadcast, route alerts & targeted bill publication notices)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Notifications')
BEGIN
    PRINT 'Creating table Notifications...';
    CREATE TABLE [dbo].[Notifications] (
        [NotificationId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Notifications] PRIMARY KEY CLUSTERED,
        [Title] NVARCHAR(100) NOT NULL,
        [Message] NVARCHAR(1000) NOT NULL,
        [NotificationType] NVARCHAR(30) NOT NULL CONSTRAINT [CK_Notifications_NotificationType] CHECK ([NotificationType] IN ('BillPublished', 'BillUpdated', 'GeneralAnnouncement', 'VacationUpdate')),
        [TargetRole] NVARCHAR(20) NOT NULL CONSTRAINT [CK_Notifications_TargetRole] CHECK ([TargetRole] IN ('All', 'DeliveryBoy', 'Admin')),
        [TargetRouteId] INT NULL CONSTRAINT [FK_Notifications_TargetRouteId] REFERENCES [dbo].[Routes]([RouteId]),
        [TargetDeliveryBoyId] INT NULL CONSTRAINT [FK_Notifications_TargetDeliveryBoyId] REFERENCES [dbo].[DeliveryBoys]([DeliveryBoyId]),
        [CustomerId] INT NULL CONSTRAINT [FK_Notifications_CustomerId] REFERENCES [dbo].[Customers]([CustomerId]),
        [BillId] BIGINT NULL CONSTRAINT [FK_Notifications_BillId] REFERENCES [dbo].[MonthlyBills]([BillId]),
        [CustomerNameSnapshot] NVARCHAR(100) NULL,
        [UpdatedBillAmount] DECIMAL(18,2) NULL,
        [NewTotalOutstandingBalance] DECIMAL(18,2) NULL,
        [BillingMonthYear] VARCHAR(10) NULL,
        [PendingMonthsCount] INT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Notifications_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_Notifications_CreatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 20. NotificationAcknowledgements (Delivery boy read/seen acknowledgement tracking)
-- Allowed CASCADE DELETE from Notifications as ephemeral dependent receipt
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'NotificationAcknowledgements')
BEGIN
    PRINT 'Creating table NotificationAcknowledgements...';
    CREATE TABLE [dbo].[NotificationAcknowledgements] (
        [AcknowledgementId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_NotificationAcknowledgements] PRIMARY KEY CLUSTERED,
        [NotificationId] BIGINT NOT NULL CONSTRAINT [FK_NotificationAcks_NotificationId] REFERENCES [dbo].[Notifications]([NotificationId]) ON DELETE CASCADE,
        [UserId] INT NOT NULL CONSTRAINT [FK_NotificationAcks_UserId] REFERENCES [dbo].[Users]([UserId]),
        [AcknowledgedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_NotificationAcks_AcknowledgedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

-- 21. AppSettings (Global system configuration & enterprise parameters)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AppSettings')
BEGIN
    PRINT 'Creating table AppSettings...';
    CREATE TABLE [dbo].[AppSettings] (
        [SettingId] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AppSettings] PRIMARY KEY CLUSTERED,
        [SettingKey] VARCHAR(50) NOT NULL CONSTRAINT [UQ_AppSettings_SettingKey] UNIQUE,
        [SettingValue] NVARCHAR(500) NOT NULL,
        [DataType] VARCHAR(20) NOT NULL CONSTRAINT [DF_AppSettings_DataType] DEFAULT ('String'),
        [Description] NVARCHAR(250) NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_AppSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedBy] INT NOT NULL CONSTRAINT [FK_AppSettings_UpdatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

-- 22. AuditLogs (Administrative change capture & JSON state diffs)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
BEGIN
    PRINT 'Creating table AuditLogs...';
    CREATE TABLE [dbo].[AuditLogs] (
        [AuditLogId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED,
        [UserId] INT NULL CONSTRAINT [FK_AuditLogs_UserId] REFERENCES [dbo].[Users]([UserId]),
        [Action] VARCHAR(50) NOT NULL,
        [TableName] VARCHAR(100) NOT NULL,
        [RecordId] VARCHAR(50) NOT NULL,
        [OldValuesJson] NVARCHAR(MAX) NULL,
        [NewValuesJson] NVARCHAR(MAX) NULL,
        [IpAddress] VARCHAR(45) NULL,
        [Timestamp] DATETIME2(7) NOT NULL CONSTRAINT [DF_AuditLogs_Timestamp] DEFAULT (SYSUTCDATETIME())
    );
END
GO

PRINT 'All 22 tables created or verified successfully.';
GO
