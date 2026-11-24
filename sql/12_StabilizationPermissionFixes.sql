-- ====================================================================================
-- Script: 12_StabilizationPermissionFixes.sql
-- Description: Targeted Database Permission Stabilization for Least-Privilege Application Role (db_dms_app)
-- Author: Kevin Gajera (Senior SQL Server Database Engineer & ADO.NET Backend Developer)
-- Baseline: f2f04fc36fae021b62932323076df57142657314
-- Target DB: DairyManagementDb (Microsoft SQL Server 2019/2022)
-- Execution Context: Database Administrator / DDL Deployment
--
-- Scope:
--   1. DEF-01: Grant narrowly scoped column-level SELECT permissions on dbo.Users to db_dms_app
--              for authentication queries (UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt).
--              Table-wide SELECT is strictly prohibited; unneeded columns (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) remain denied.
--   2. DEF-02: Grant narrowly scoped column-level SELECT permissions on dbo.CustomerDeliveryBoyAssignments to db_dms_app
--              for customer assignment history (AssignmentId, CustomerId, DeliveryBoyId, AssignedFrom, AssignedTo, IsActive, Notes, CreatedAt).
--              Unneeded columns (CreatedBy, UpdatedAt, UpdatedBy) remain denied.
--
-- Invariants Preserved:
--   - Idempotent and safe to execute multiple times.
--   - No membership changes to db_owner, db_datareader, or db_datawriter.
--   - No broad database, schema, or table-level grants on sensitive objects.
--   - Append-only financial ledger (dbo.PaymentAllocationEvents) remains DENY INSERT/UPDATE/DELETE.
--   - Zero modifications to business rules, financial calculations, or stored procedure logic.
--   - Zero data deletion, truncation, or schema resetting.
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
PRINT 'Starting Targeted Permission Stabilization Deployment (DEF-01 & DEF-02)...';
PRINT '====================================================================================';
GO

-- ------------------------------------------------------------------------------------
-- 1. DEF-01: Users Authentication Column-Level SELECT Permissions
-- ------------------------------------------------------------------------------------
PRINT 'Applying DEF-01: Granting column-level SELECT on dbo.Users to db_dms_app...';

-- The authentication repository (UserAuthRepository.cs) executes:
--   SELECT UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt
--   FROM [dbo].[Users]
--   WHERE ([Username] = @Username AND [Phone] = @Phone AND [Role] = 'Admin' AND [IsActive] = 1)
--      OR ([Phone] = @Phone AND [Role] = 'DeliveryBoy' AND [IsActive] = 1);
--
-- Only the columns required to verify identity, credentials, active status, and audit timestamps are granted.
-- Sensitive audit metadata columns (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) are NOT granted.
-- Table-wide SELECT on dbo.Users remains withheld.

GRANT SELECT (
    [UserId],
    [Username],
    [PasswordHash],
    [Role],
    [FullName],
    [Phone],
    [IsActive],
    [LastLoginAt]
) ON [dbo].[Users] TO [db_dms_app];
GO

PRINT 'DEF-01 applied successfully.';
GO

-- ------------------------------------------------------------------------------------
-- 2. DEF-02: Customer Assignment History Column-Level SELECT Permissions
-- ------------------------------------------------------------------------------------
PRINT 'Applying DEF-02: Granting column-level SELECT on dbo.CustomerDeliveryBoyAssignments to db_dms_app...';

-- CustomerRepository.cs (GetAssignmentHistoryAsync) executes:
--   SELECT a.AssignmentId, a.DeliveryBoyId, u.FullName AS DeliveryBoyName,
--          a.AssignedFrom, a.AssignedTo, a.IsActive, a.Notes, a.CreatedAt
--   FROM [dbo].[CustomerDeliveryBoyAssignments] a
--   INNER JOIN [dbo].[DeliveryBoys] db ON a.DeliveryBoyId = db.DeliveryBoyId
--   INNER JOIN [dbo].[Users] u ON db.UserId = u.UserId
--   WHERE a.CustomerId = @CustomerId
--   ORDER BY a.AssignedFrom DESC, a.AssignmentId DESC;
--
-- The previous migration (06_CustomerManagementEnhancements.sql) granted INSERT, UPDATE, DELETE to db_dms_app
-- but omitted SELECT.
-- Here we grant column-level SELECT strictly on the 8 columns required to project assignment history and satisfy the WHERE filter.
-- Unneeded audit columns (CreatedBy, UpdatedAt, UpdatedBy) are NOT granted SELECT.

GRANT SELECT (
    [AssignmentId],
    [CustomerId],
    [DeliveryBoyId],
    [AssignedFrom],
    [AssignedTo],
    [IsActive],
    [Notes],
    [CreatedAt]
) ON [dbo].[CustomerDeliveryBoyAssignments] TO [db_dms_app];
GO

PRINT 'DEF-02 applied successfully.';
GO

-- ------------------------------------------------------------------------------------
-- 3. Verification & Diagnostic Confirmation
-- ------------------------------------------------------------------------------------
PRINT 'Confirming active column-level grants for db_dms_app...';

SELECT 
    pr.name AS [Principal],
    pe.state_desc AS [State],
    pe.permission_name AS [Permission],
    OBJECT_NAME(pe.major_id) AS [Object],
    COL_NAME(pe.major_id, pe.minor_id) AS [Column]
FROM sys.database_permissions pe
INNER JOIN sys.database_principals pr ON pe.grantee_principal_id = pr.principal_id
WHERE pr.name = 'db_dms_app' 
  AND pe.major_id IN (OBJECT_ID('dbo.Users'), OBJECT_ID('dbo.CustomerDeliveryBoyAssignments'))
  AND pe.permission_name = 'SELECT'
ORDER BY [Object], [Column];
GO

PRINT '====================================================================================';
PRINT 'Targeted Permission Stabilization Deployment completed successfully.';
PRINT '====================================================================================';
GO
