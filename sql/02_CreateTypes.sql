-- ====================================================================================
-- Script 02: User-Defined Table Types (UDTT)
-- Project: Dairy Management System (DMS)
-- Database: DairyManagementDb
-- Section: 8.3 User-Defined Table Types (UDTT)
-- Author: Senior SQL Server Database Architect & ADO.NET Backend Engineer
-- Description: Creates strongly-typed table types for Table-Valued Parameters (TVP)
--              in ADO.NET, preventing string concatenation and dynamic SQL. Safe and repeatable.
-- ====================================================================================

USE [DairyManagementDb];
GO

-- 1. Generic BigInt ID List (Used for batch operations, batch status evaluations, route assignments)
IF NOT EXISTS (SELECT 1 FROM sys.types WHERE name = N'BigIntIdList' AND is_table_type = 1)
BEGIN
    PRINT 'Creating User-Defined Table Type dbo.BigIntIdList...';
    CREATE TYPE [dbo].[BigIntIdList] AS TABLE (
        [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
    );
END
ELSE
BEGIN
    PRINT 'User-Defined Table Type dbo.BigIntIdList already exists.';
END
GO

-- 2. Payment Allocation Batch Type (Used for atomic multi-bill payment distribution)
IF NOT EXISTS (SELECT 1 FROM sys.types WHERE name = N'PaymentAllocationItemType' AND is_table_type = 1)
BEGIN
    PRINT 'Creating User-Defined Table Type dbo.PaymentAllocationItemType...';
    CREATE TYPE [dbo].[PaymentAllocationItemType] AS TABLE (
        [BillId] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
        [AllocatedAmount] DECIMAL(18,2) NOT NULL CHECK ([AllocatedAmount] > 0)
    );
END
ELSE
BEGIN
    PRINT 'User-Defined Table Type dbo.PaymentAllocationItemType already exists.';
END
GO
