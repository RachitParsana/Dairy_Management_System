-- ====================================================================================
-- Dairy Management System — Day 5 Architecture Gap Correction
-- Script 09: Dedicated Read-Only Route Lookup Stored Procedure & Security Grants
-- Architect: Rachit Parsana (Lead Project Architect)
-- Engineer: Kevin Gajera (Backend / SQL Server Developer)
-- Date: 20-Nov-2026
-- Description: Production-grade, idempotent deployment script for active Route lookups.
--              Provisions compiled stored procedure dbo.usp_Routes_GetActiveLookup,
--              enforces least-privilege execution via db_dms_app, returns all active
--              routes independently of customer assignments, and strictly preserves
--              PaymentAllocationEvents ledger DENY protections.
--
-- TARGET DATABASE & EXECUTION ENVIRONMENT:
-- 1. Caller must explicitly specify the target database context (e.g., sqlcmd -d <DatabaseName> -b).
--    This script does NOT hardcode USE [DatabaseName] to prevent silent environment redirects.
-- 2. Safe for execution in both SQLCMD and interactive SSMS query windows:
--    Preconditions are evaluated atomically before DDL execution; failures throw a terminating
--    T-SQL exception (THROW), preventing procedure creation and permission grants without
--    resorting to SET NOEXEC ON. This guarantees the interactive session is never left in NOEXEC mode.
-- ====================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting 09_RouteLookup deployment...';
PRINT 'Target Database Context: ' + DB_NAME();
PRINT '====================================================================================';

-- ====================================================================================
-- 1. Pre-Deployment Safety & Environment Gates
-- - Disallows execution against system databases (master, model, msdb, tempdb).
-- - Verifies mandatory application role [db_dms_app] exists.
-- - Throws terminating error on precondition failure, preventing subsequent DDL execution.
-- ====================================================================================
IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
BEGIN
    DECLARE @SystemDbMsg NVARCHAR(512) = N'Deployment halted: Invalid target database [' + DB_NAME() + N']. Cannot deploy to system databases. Specify a valid user database (e.g. -d DairyManagementDb_Test or -d DairyManagementDb).';
    THROW 50000, @SystemDbMsg, 1;
END;

IF DATABASE_PRINCIPAL_ID(N'db_dms_app') IS NULL
BEGIN
    DECLARE @MissingRoleMsg NVARCHAR(512) = N'Deployment failed: Mandatory database role [db_dms_app] does not exist in database [' + DB_NAME() + N']. Stored procedure dbo.usp_Routes_GetActiveLookup requires EXECUTE permission granted to [db_dms_app]. Deployment halted.';
    THROW 50001, @MissingRoleMsg, 1;
END;

-- ====================================================================================
-- 2. Stored Procedure: dbo.usp_Routes_GetActiveLookup
-- Retrieves all active routes from dbo.Routes ordered by RouteName ascending.
-- Returns RouteId, RouteCode, RouteName directly from master table.
-- Independent of customer assignments (includes routes with 0 customers).
-- Excludes inactive routes (IsActive = 0).
-- Executed dynamically within the guarded batch to prevent compilation/execution on gate failure.
-- ====================================================================================
PRINT 'Deploying stored procedure dbo.usp_Routes_GetActiveLookup in [' + DB_NAME() + ']...';

EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_Routes_GetActiveLookup]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        r.[RouteId],
        r.[RouteCode],
        r.[RouteName]
    FROM [dbo].[Routes] r
    WHERE r.[IsActive] = 1
    ORDER BY r.[RouteName] ASC;
END;
';

PRINT 'Stored procedure dbo.usp_Routes_GetActiveLookup deployed successfully in [' + DB_NAME() + '].';

-- ====================================================================================
-- 3. Security Permissions & Least-Privilege Grants
-- Grant EXECUTE exclusively to application role db_dms_app.
-- Avoid granting direct write or elevated permissions.
-- ====================================================================================
PRINT 'Configuring security permissions for dbo.usp_Routes_GetActiveLookup in [' + DB_NAME() + ']...';

GRANT EXECUTE ON [dbo].[usp_Routes_GetActiveLookup] TO [db_dms_app];
PRINT 'Granted EXECUTE on dbo.usp_Routes_GetActiveLookup to db_dms_app in [' + DB_NAME() + '].';

-- ====================================================================================
-- 4. Post-Deployment Verification Gate
-- Verify that EXECUTE permission on dbo.usp_Routes_GetActiveLookup is actively granted.
-- ====================================================================================
IF NOT EXISTS (
    SELECT 1 
    FROM sys.database_permissions dp
    WHERE dp.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'db_dms_app')
      AND dp.major_id = OBJECT_ID(N'[dbo].[usp_Routes_GetActiveLookup]')
      AND dp.permission_name = N'EXECUTE'
      AND dp.state = 'G'
)
BEGIN
    DECLARE @GrantFailMsg NVARCHAR(512) = N'Deployment verification failed: Permission EXECUTE was not verified for [db_dms_app] on [dbo].[usp_Routes_GetActiveLookup] in database [' + DB_NAME() + N'].';
    THROW 50002, @GrantFailMsg, 1;
END;

PRINT '====================================================================================';
PRINT '09_RouteLookup deployment completed successfully on [' + DB_NAME() + ']!';
PRINT '====================================================================================';
GO
