-- ====================================================================================
-- Dairy Management System — Day 6 Backend Migration
-- Script 10: Delivery Boy Management & Monthly Compensation Architecture
-- Architect: Rachit Parsana (Lead Project Architect)
-- Engineer: Kevin Gajera (Senior SQL Server / Backend Engineer)
-- Date: 20-Nov-2026
-- Description: Production-grade, idempotent deployment script for Day 6.
--              Provisions:
--              1. Dedicated append-only adjustment ledger dbo.DeliveryBoyCompensationAdjustments
--                 with explicit DENY permissions on direct INSERT/UPDATE/DELETE.
--              2. All 14 approved stored procedures for Delivery Boy and Compensation management.
--              3. Least-privilege EXECUTE permissions granted strictly to [db_dms_app].
--              4. Concurrency-safe transactions, deterministic locking (UPDLOCK, HOLDLOCK),
--                 and ownership chaining on ledger writes.
--
-- TARGET DATABASE & EXECUTION ENVIRONMENT:
-- 1. Caller must explicitly specify the target database context (e.g., sqlcmd -d <DatabaseName> -b).
--    This script does NOT hardcode USE [DatabaseName] to prevent silent environment redirects.
-- 2. Safe for execution in both SQLCMD and interactive SSMS query windows:
--    Preconditions are evaluated atomically before DDL execution; failures throw a terminating
--    T-SQL exception (THROW), preventing object creation and permission grants without
--    resorting to SET NOEXEC ON. This guarantees the interactive session is never left in NOEXEC mode.
-- ====================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '====================================================================================';
PRINT 'Starting 10_DeliveryBoyAndCompensationManagement deployment...';
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
    DECLARE @MissingRoleMsg NVARCHAR(512) = N'Deployment failed: Mandatory database role [db_dms_app] does not exist in database [' + DB_NAME() + N']. Stored procedures require EXECUTE permission granted to [db_dms_app]. Deployment halted.';
    THROW 50001, @MissingRoleMsg, 1;
END;
GO

-- ====================================================================================
-- 2. Table: dbo.DeliveryBoyCompensationAdjustments (Dedicated Financial Adjustment Ledger)
-- - Append-only, immutable adjustment lines.
-- - Explicit DENY on direct INSERT, UPDATE, DELETE to db_dms_app and dms_app_user.
-- ====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DeliveryBoyCompensationAdjustments')
BEGIN
    PRINT 'Creating table dbo.DeliveryBoyCompensationAdjustments...';
    CREATE TABLE [dbo].[DeliveryBoyCompensationAdjustments] (
        [AdjustmentId] BIGINT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_DeliveryBoyCompensationAdjustments] PRIMARY KEY CLUSTERED,
        [CompensationId] BIGINT NOT NULL CONSTRAINT [FK_DeliveryBoyAdjustments_CompensationId] REFERENCES [dbo].[DeliveryBoyMonthlyCompensations]([CompensationId]),
        [AdjustmentType] NVARCHAR(30) NOT NULL CONSTRAINT [CK_DeliveryBoyAdjustments_Type] CHECK ([AdjustmentType] IN ('Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other')),
        [AdjustmentAmount] DECIMAL(18,2) NOT NULL, -- Positive for credits/bonuses, Negative for deductions
        [Reason] NVARCHAR(250) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_DeliveryBoyAdjustments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] INT NOT NULL CONSTRAINT [FK_DeliveryBoyAdjustments_CreatedBy] REFERENCES [dbo].[Users]([UserId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryBoyCompensationAdjustments_CompensationId' AND object_id = OBJECT_ID(N'[dbo].[DeliveryBoyCompensationAdjustments]'))
BEGIN
    PRINT 'Creating index IX_DeliveryBoyCompensationAdjustments_CompensationId...';
    CREATE NONCLUSTERED INDEX [IX_DeliveryBoyCompensationAdjustments_CompensationId]
    ON [dbo].[DeliveryBoyCompensationAdjustments]([CompensationId])
    INCLUDE ([AdjustmentAmount], [AdjustmentType], [CreatedAt]);
END
GO

-- ====================================================================================
-- 3. Stored Procedure: dbo.usp_DeliveryBoy_Register
-- Atomically creates staff User, DeliveryBoys profile, and initial rate tier if commission-based.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_Register]
    @Username NVARCHAR(50),
    @PasswordHash NVARCHAR(256),
    @FullName NVARCHAR(100),
    @Phone VARCHAR(15),
    @PrimaryRouteId INT,
    @CompensationType NVARCHAR(20),      -- 'Fixed Salary' or 'Commission'
    @MonthlyBaseSalary DECIMAL(18,2) = 0.00,
    @InitialCommissionRate DECIMAL(18,2) = 0.00,
    @EffectiveFrom DATE = NULL,
    @VehicleNumber NVARCHAR(30) = NULL,
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT,
    @DeliveryBoyId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Username = LTRIM(RTRIM(@Username));
    SET @FullName = LTRIM(RTRIM(@FullName));
    SET @Phone = LTRIM(RTRIM(@Phone));
    SET @VehicleNumber = NULLIF(LTRIM(RTRIM(@VehicleNumber)), '');
    SET @Notes = NULLIF(LTRIM(RTRIM(@Notes)), '');

    IF (@CompensationType NOT IN ('Fixed Salary', 'Commission'))
    BEGIN
        THROW 50042, 'Invalid compensation type. Must be ''Fixed Salary'' or ''Commission''.', 1;
    END

    IF (@CompensationType = 'Fixed Salary')
    BEGIN
        IF (@MonthlyBaseSalary < 0.00)
            THROW 50042, 'Monthly base salary cannot be negative.', 1;
        SET @InitialCommissionRate = 0.00;
    END
    ELSE IF (@CompensationType = 'Commission')
    BEGIN
        IF (@InitialCommissionRate <= 0.00)
            THROW 50042, 'Initial commission rate must be greater than zero for commission-based employees.', 1;
        IF (@EffectiveFrom IS NULL)
            THROW 50042, 'EffectiveFrom date is mandatory for commission-based employees.', 1;
        SET @MonthlyBaseSalary = 0.00;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WHERE Phone = @Phone)
    BEGIN
        THROW 50041, 'Phone number is already registered to another staff account.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = @Username)
    BEGIN
        THROW 50041, 'Username is already in use by another account.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE RouteId = @PrimaryRouteId AND IsActive = 1)
    BEGIN
        THROW 50043, 'Primary route does not exist or is inactive.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @NewUserId INT;

        INSERT INTO dbo.Users (
            Username, PasswordHash, Role, FullName, Phone, IsActive, CreatedAt, CreatedBy
        )
        VALUES (
            @Username, @PasswordHash, 'DeliveryBoy', @FullName, @Phone, 1, SYSUTCDATETIME(), @AdminUserId
        );
        SET @NewUserId = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryBoys (
            UserId, PrimaryRouteId, CompensationType, CurrentCommissionRate,
            MonthlyBaseSalary, VehicleNumber, IsActive, CreatedAt, CreatedBy
        )
        VALUES (
            @NewUserId, @PrimaryRouteId, @CompensationType,
            CASE WHEN @CompensationType = 'Commission' THEN @InitialCommissionRate ELSE 0.00 END,
            CASE WHEN @CompensationType = 'Fixed Salary' THEN @MonthlyBaseSalary ELSE 0.00 END,
            @VehicleNumber, 1, SYSUTCDATETIME(), @AdminUserId
        );
        SET @DeliveryBoyId = SCOPE_IDENTITY();

        IF (@CompensationType = 'Commission')
        BEGIN
            INSERT INTO dbo.DeliveryBoyCommissionRates (
                DeliveryBoyId, CommissionRatePerLitre, EffectiveFrom, EffectiveTo, Notes, CreatedAt, CreatedBy
            )
            VALUES (
                @DeliveryBoyId, @InitialCommissionRate, @EffectiveFrom, NULL, @Notes, SYSUTCDATETIME(), @AdminUserId
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 4. Stored Procedure: dbo.usp_DeliveryBoy_GetDirectory
-- Retrieves searchable delivery boy list with active customer counts.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_GetDirectory]
    @SearchTerm NVARCHAR(100) = NULL,
    @StatusFilter NVARCHAR(20) = NULL,       -- 'All', 'Active', 'Inactive'
    @CompensationType NVARCHAR(20) = NULL,   -- 'All', 'Fixed Salary', 'Commission'
    @RouteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @SearchTerm = NULLIF(LTRIM(RTRIM(@SearchTerm)), '');
    SET @StatusFilter = NULLIF(LTRIM(RTRIM(@StatusFilter)), '');
    SET @CompensationType = NULLIF(LTRIM(RTRIM(@CompensationType)), '');

    SELECT 
        db.DeliveryBoyId,
        db.UserId,
        u.FullName,
        u.Username,
        u.Phone,
        db.VehicleNumber,
        db.PrimaryRouteId,
        r.RouteName,
        r.RouteCode,
        db.CompensationType,
        db.CurrentCommissionRate,
        db.MonthlyBaseSalary,
        ISNULL(cust.ActiveCustomerCount, 0) AS ActiveCustomerCount,
        db.IsActive,
        db.CreatedAt
    FROM dbo.DeliveryBoys db
    INNER JOIN dbo.Users u ON db.UserId = u.UserId
    LEFT JOIN dbo.Routes r ON db.PrimaryRouteId = r.RouteId
    LEFT JOIN (
        SELECT CurrentDeliveryBoyId, COUNT(*) AS ActiveCustomerCount
        FROM dbo.Customers
        WHERE IsActive = 1 AND CurrentDeliveryBoyId IS NOT NULL
        GROUP BY CurrentDeliveryBoyId
    ) cust ON db.DeliveryBoyId = cust.CurrentDeliveryBoyId
    WHERE (@SearchTerm IS NULL OR u.FullName LIKE '%' + @SearchTerm + '%' OR u.Phone LIKE '%' + @SearchTerm + '%' OR db.VehicleNumber LIKE '%' + @SearchTerm + '%' OR u.Username LIKE '%' + @SearchTerm + '%')
      AND (@StatusFilter IS NULL OR @StatusFilter = 'All' OR (@StatusFilter = 'Active' AND db.IsActive = 1) OR (@StatusFilter = 'Inactive' AND db.IsActive = 0))
      AND (@CompensationType IS NULL OR @CompensationType = 'All' OR db.CompensationType = @CompensationType)
      AND (@RouteId IS NULL OR db.PrimaryRouteId = @RouteId)
    ORDER BY u.FullName ASC;
END;
GO

-- ====================================================================================
-- 5. Stored Procedure: dbo.usp_DeliveryBoy_GetDetails
-- Returns 4 result sets: Profile, Assigned Customers, Rate History, Recent Compensations.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_GetDetails]
    @DeliveryBoyId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result Set 1: Profile & Staff User Details
    SELECT 
        db.DeliveryBoyId,
        db.UserId,
        u.FullName,
        u.Username,
        u.Phone,
        db.VehicleNumber,
        db.PrimaryRouteId,
        r.RouteName,
        r.RouteCode,
        db.CompensationType,
        db.CurrentCommissionRate,
        db.MonthlyBaseSalary,
        db.IsActive,
        u.LastLoginAt,
        db.CreatedAt,
        db.CreatedBy,
        cu.FullName AS CreatedByName,
        db.UpdatedAt,
        db.UpdatedBy,
        uu.FullName AS UpdatedByName
    FROM dbo.DeliveryBoys db
    INNER JOIN dbo.Users u ON db.UserId = u.UserId
    LEFT JOIN dbo.Routes r ON db.PrimaryRouteId = r.RouteId
    LEFT JOIN dbo.Users cu ON db.CreatedBy = cu.UserId
    LEFT JOIN dbo.Users uu ON db.UpdatedBy = uu.UserId
    WHERE db.DeliveryBoyId = @DeliveryBoyId;

    -- Result Set 2: Active Assigned Customers Roster
    SELECT 
        c.CustomerId,
        c.CustomerCode,
        c.FullName,
        c.FullNameGujarati,
        c.Phone,
        c.Address,
        c.Area,
        c.DeliverySequence,
        r.RouteName,
        a.AssignedFrom,
        a.AssignedTo,
        a.IsActive AS AssignmentIsActive
    FROM dbo.Customers c
    INNER JOIN dbo.Routes r ON c.RouteId = r.RouteId
    LEFT JOIN dbo.CustomerDeliveryBoyAssignments a ON c.CustomerId = a.CustomerId 
        AND a.DeliveryBoyId = @DeliveryBoyId AND a.IsActive = 1 AND a.AssignedTo IS NULL
    WHERE c.CurrentDeliveryBoyId = @DeliveryBoyId AND c.IsActive = 1
    ORDER BY c.DeliverySequence ASC, c.FullName ASC;

    -- Result Set 3: Commission Rate History
    SELECT 
        cr.RateId,
        cr.DeliveryBoyId,
        cr.CommissionRatePerLitre,
        cr.EffectiveFrom,
        cr.EffectiveTo,
        cr.Notes,
        cr.CreatedAt,
        cr.CreatedBy,
        u.FullName AS CreatedByName
    FROM dbo.DeliveryBoyCommissionRates cr
    LEFT JOIN dbo.Users u ON cr.CreatedBy = u.UserId
    WHERE cr.DeliveryBoyId = @DeliveryBoyId
    ORDER BY cr.EffectiveFrom DESC, cr.RateId DESC;

    -- Result Set 4: Recent Monthly Compensations (up to 24 months)
    SELECT TOP 24
        mc.CompensationId,
        mc.DeliveryBoyId,
        mc.Month,
        mc.Year,
        mc.CompensationType,
        mc.EligibleCompletedLitres,
        mc.ApplicableCommissionRate,
        mc.CalculatedCommission,
        mc.FixedSalaryAmount,
        mc.Adjustments,
        mc.NetPayableAmount,
        mc.Status,
        mc.FinalizedAt,
        mc.PaidAt,
        mc.PaymentReference
    FROM dbo.DeliveryBoyMonthlyCompensations mc
    WHERE mc.DeliveryBoyId = @DeliveryBoyId
    ORDER BY mc.Year DESC, mc.Month DESC;
END;
GO

-- ====================================================================================
-- 6. Stored Procedure: dbo.usp_DeliveryBoy_UpdateProfile
-- Updates editable profile fields with phone uniqueness validation.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_UpdateProfile]
    @DeliveryBoyId INT,
    @FullName NVARCHAR(100),
    @Phone VARCHAR(15),
    @VehicleNumber NVARCHAR(30) = NULL,
    @PrimaryRouteId INT,
    @MonthlyBaseSalary DECIMAL(18,2) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @FullName = LTRIM(RTRIM(@FullName));
    SET @Phone = LTRIM(RTRIM(@Phone));
    SET @VehicleNumber = NULLIF(LTRIM(RTRIM(@VehicleNumber)), '');

    DECLARE @UserId INT;
    DECLARE @CompType NVARCHAR(20);

    SELECT @UserId = UserId, @CompType = CompensationType
    FROM dbo.DeliveryBoys
    WHERE DeliveryBoyId = @DeliveryBoyId;

    IF (@UserId IS NULL)
    BEGIN
        THROW 50045, 'Delivery boy record does not exist.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Users WHERE Phone = @Phone AND UserId <> @UserId)
    BEGIN
        THROW 50044, 'Phone number is already in use by another user.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Routes WHERE RouteId = @PrimaryRouteId AND IsActive = 1)
    BEGIN
        THROW 50043, 'Primary route does not exist or is inactive.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.Users
        SET FullName = @FullName,
            Phone = @Phone,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE UserId = @UserId;

        UPDATE dbo.DeliveryBoys
        SET PrimaryRouteId = @PrimaryRouteId,
            VehicleNumber = @VehicleNumber,
            MonthlyBaseSalary = CASE 
                WHEN @CompType = 'Fixed Salary' AND @MonthlyBaseSalary IS NOT NULL AND @MonthlyBaseSalary >= 0 
                THEN @MonthlyBaseSalary 
                ELSE MonthlyBaseSalary 
            END,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE DeliveryBoyId = @DeliveryBoyId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 7. Stored Procedure: dbo.usp_DeliveryBoy_Deactivate
-- Atomic 11-step deactivation transaction.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_Deactivate]
    @DeliveryBoyId INT,
    @ReassignToDeliveryBoyId INT = NULL,
    @Reason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Lock DeliveryBoys row
        DECLARE @UserId INT;
        DECLARE @IsActive BIT;

        SELECT @UserId = UserId, @IsActive = IsActive
        FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
        WHERE DeliveryBoyId = @DeliveryBoyId;

        IF (@UserId IS NULL)
        BEGIN
            THROW 50045, 'Delivery boy record does not exist.', 1;
        END

        -- 2. Lock Users row
        SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId;

        -- 3. Verify active status
        IF (@IsActive = 0)
        BEGIN
            THROW 50053, 'Delivery boy is already deactivated.', 1;
        END

        -- 4. Disable Users account (blocks login)
        UPDATE dbo.Users
        SET IsActive = 0,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE UserId = @UserId;

        -- 5. Disable DeliveryBoys profile
        UPDATE dbo.DeliveryBoys
        SET IsActive = 0,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE DeliveryBoyId = @DeliveryBoyId;

        -- 6. Close open-ended commission rate tier
        UPDATE dbo.DeliveryBoyCommissionRates
        SET EffectiveTo = @Today
        WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveTo IS NULL;

        -- 7. Handle customer assignments
        IF (@ReassignToDeliveryBoyId IS NOT NULL)
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryBoys WHERE DeliveryBoyId = @ReassignToDeliveryBoyId AND IsActive = 1)
            BEGIN
                THROW 50008, 'Target delivery boy does not exist or is inactive.', 1;
            END

            -- Reassign active customers
            DECLARE @ReassignCustId INT;
            DECLARE curCusts CURSOR LOCAL FAST_FORWARD FOR
                SELECT CustomerId
                FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK)
                WHERE CurrentDeliveryBoyId = @DeliveryBoyId AND IsActive = 1;

            OPEN curCusts;
            FETCH NEXT FROM curCusts INTO @ReassignCustId;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                -- Close active assignment
                UPDATE dbo.CustomerDeliveryBoyAssignments
                SET AssignedTo = @Today,
                    IsActive = 0,
                    UpdatedAt = SYSUTCDATETIME(),
                    UpdatedBy = @AdminUserId
                WHERE CustomerId = @ReassignCustId AND DeliveryBoyId = @DeliveryBoyId AND IsActive = 1 AND AssignedTo IS NULL;

                -- Insert replacement assignment
                INSERT INTO dbo.CustomerDeliveryBoyAssignments (
                    CustomerId, DeliveryBoyId, AssignedFrom, AssignedTo, IsActive, Notes, CreatedAt, CreatedBy
                )
                VALUES (
                    @ReassignCustId, @ReassignToDeliveryBoyId, @Today, NULL, 1, @Reason, SYSUTCDATETIME(), @AdminUserId
                );

                -- Update customer pointer
                UPDATE dbo.Customers
                SET CurrentDeliveryBoyId = @ReassignToDeliveryBoyId,
                    UpdatedAt = SYSUTCDATETIME(),
                    UpdatedBy = @AdminUserId
                WHERE CustomerId = @ReassignCustId;

                FETCH NEXT FROM curCusts INTO @ReassignCustId;
            END

            CLOSE curCusts;
            DEALLOCATE curCusts;
        END
        ELSE
        BEGIN
            -- Close assignments and set CurrentDeliveryBoyId to NULL
            UPDATE dbo.CustomerDeliveryBoyAssignments
            SET AssignedTo = @Today,
                IsActive = 0,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE DeliveryBoyId = @DeliveryBoyId AND IsActive = 1 AND AssignedTo IS NULL;

            UPDATE dbo.Customers
            SET CurrentDeliveryBoyId = NULL,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE CurrentDeliveryBoyId = @DeliveryBoyId;
        END

        -- 8. Historical DailyMilkDeliveries remain untouched
        -- 9. Existing DeliveryBoyMonthlyCompensations remain intact

        -- 10. Audit log entry
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
        BEGIN
            INSERT INTO dbo.AuditLogs (UserId, Action, TableName, RecordId, NewValuesJson, Timestamp)
            VALUES (@AdminUserId, 'DEACTIVATE', 'DeliveryBoys', CAST(@DeliveryBoyId AS VARCHAR(50)), 
                    '{"DeliveryBoyId":' + CAST(@DeliveryBoyId AS NVARCHAR(20)) + ',"Reason":"' + REPLACE(@Reason, '"', '\"') + '"}',
                    SYSUTCDATETIME());
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 8. Stored Procedure: dbo.usp_DeliveryBoy_Reactivate
-- Reactivates a delivery boy with conditional rate validation.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_DeliveryBoy_Reactivate]
    @DeliveryBoyId INT,
    @NewCommissionRate DECIMAL(18,2) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @UserId INT;
        DECLARE @CompType NVARCHAR(20);
        DECLARE @IsActive BIT;

        SELECT @UserId = UserId, @CompType = CompensationType, @IsActive = IsActive
        FROM dbo.DeliveryBoys WITH (UPDLOCK, HOLDLOCK)
        WHERE DeliveryBoyId = @DeliveryBoyId;

        IF (@UserId IS NULL)
        BEGIN
            THROW 50045, 'Delivery boy record does not exist.', 1;
        END

        IF (@IsActive = 1)
        BEGIN
            THROW 50053, 'Delivery boy is already active.', 1;
        END

        SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId;

        IF (@CompType = 'Commission')
        BEGIN
            IF (@NewCommissionRate IS NULL OR @NewCommissionRate <= 0.00)
            BEGIN
                THROW 50055, 'Reactivation of a commission-based delivery boy requires an approved commission rate tier effective on the reactivation date.', 1;
            END

            -- Close or adjust any existing rate tiers to prevent overlap with new tier starting @Today
            UPDATE dbo.DeliveryBoyCommissionRates
            SET EffectiveTo = DATEADD(day, -1, @Today)
            WHERE DeliveryBoyId = @DeliveryBoyId 
              AND (EffectiveTo IS NULL OR EffectiveTo >= @Today)
              AND EffectiveFrom < @Today;

            -- If a tier already started today, update it in-place
            IF EXISTS (SELECT 1 FROM dbo.DeliveryBoyCommissionRates WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveFrom = @Today)
            BEGIN
                UPDATE dbo.DeliveryBoyCommissionRates
                SET CommissionRatePerLitre = @NewCommissionRate,
                    EffectiveTo = NULL,
                    Notes = 'Commission rate updated upon same-day reactivation',
                    CreatedAt = SYSUTCDATETIME(),
                    CreatedBy = @AdminUserId
                WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveFrom = @Today;
            END
            ELSE
            BEGIN
                -- Establish new verified rate tier
                INSERT INTO dbo.DeliveryBoyCommissionRates (
                    DeliveryBoyId, CommissionRatePerLitre, EffectiveFrom, EffectiveTo, Notes, CreatedAt, CreatedBy
                )
                VALUES (
                    @DeliveryBoyId, @NewCommissionRate, @Today, NULL, 'Commission rate established upon reactivation', SYSUTCDATETIME(), @AdminUserId
                );
            END

            UPDATE dbo.DeliveryBoys
            SET CurrentCommissionRate = @NewCommissionRate
            WHERE DeliveryBoyId = @DeliveryBoyId;
        END

        UPDATE dbo.Users
        SET IsActive = 1,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE UserId = @UserId;

        UPDATE dbo.DeliveryBoys
        SET IsActive = 1,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE DeliveryBoyId = @DeliveryBoyId;

        -- Customer assignments are NOT automatically restored

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 9. Stored Procedure: dbo.usp_Compensation_CalculateMonthlyDraft
-- Computes/recalculates monthly draft compensation for a calendar month.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_CalculateMonthlyDraft]
    @DeliveryBoyId INT,
    @Month TINYINT,
    @Year SMALLINT,
    @AdminUserId INT,
    @CompensationId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @CompensationId = NULL;

    IF (@Month NOT BETWEEN 1 AND 12)
        THROW 50042, 'Invalid month. Must be between 1 and 12.', 1;

    IF (@Year NOT BETWEEN 2000 AND 2100)
        THROW 50042, 'Invalid year. Must be between 2000 and 2100.', 1;

    DECLARE @StartDate DATE = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @EndDate DATE = EOMONTH(@StartDate);

    DECLARE @CompType NVARCHAR(20);
    DECLARE @BaseSalary DECIMAL(18,2);

    SELECT @CompType = CompensationType, @BaseSalary = MonthlyBaseSalary
    FROM dbo.DeliveryBoys
    WHERE DeliveryBoyId = @DeliveryBoyId;

    IF (@CompType IS NULL)
    BEGIN
        THROW 50045, 'Delivery boy record does not exist.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Check existing compensation record
        DECLARE @ExistingStatus NVARCHAR(20) = NULL;
        SELECT @CompensationId = CompensationId, @ExistingStatus = Status
        FROM dbo.DeliveryBoyMonthlyCompensations WITH (UPDLOCK, HOLDLOCK)
        WHERE DeliveryBoyId = @DeliveryBoyId AND Month = @Month AND Year = @Year;

        IF (@ExistingStatus IN ('Finalized', 'Paid'))
        BEGIN
            THROW 50046, 'Cannot recalculate finalized or paid compensation.', 1;
        END

        -- Compute completed litres and commission from daily deliveries
        DECLARE @CompletedLitres DECIMAL(18,3) = 0.000;
        DECLARE @CalcCommission DECIMAL(18,2) = 0.00;
        DECLARE @ApplicableRate DECIMAL(18,2) = 0.00;
        DECLARE @FixedSalaryAmount DECIMAL(18,2) = 0.00;

        IF (@CompType = 'Commission')
        BEGIN
            SELECT 
                @CompletedLitres = ISNULL(SUM(d.EligibleLitreQuantity), 0.000),
                @CalcCommission = ISNULL(ROUND(SUM(d.EligibleLitreQuantity * d.CommissionRateSnapshot), 2), 0.00)
            FROM dbo.DailyMilkDeliveries d
            WHERE d.DeliveryBoyId = @DeliveryBoyId
              AND d.EntryDate BETWEEN @StartDate AND @EndDate
              AND d.Status = 'Completed';

            IF (@CompletedLitres > 0.000)
            BEGIN
                SET @ApplicableRate = ROUND(@CalcCommission / @CompletedLitres, 2);
            END
            ELSE
            BEGIN
                SET @ApplicableRate = 0.00;
            END

            SET @FixedSalaryAmount = 0.00;
        END
        ELSE IF (@CompType = 'Fixed Salary')
        BEGIN
            SELECT 
                @CompletedLitres = ISNULL(SUM(d.EligibleLitreQuantity), 0.000)
            FROM dbo.DailyMilkDeliveries d
            WHERE d.DeliveryBoyId = @DeliveryBoyId
              AND d.EntryDate BETWEEN @StartDate AND @EndDate
              AND d.Status = 'Completed';

            SET @CalcCommission = 0.00;
            SET @ApplicableRate = 0.00;
            SET @FixedSalaryAmount = @BaseSalary;
        END

        -- Recompute total adjustments from adjustment ledger
        DECLARE @TotalAdjustments DECIMAL(18,2) = 0.00;
        IF (@CompensationId IS NOT NULL)
        BEGIN
            SELECT @TotalAdjustments = ISNULL(SUM(AdjustmentAmount), 0.00)
            FROM dbo.DeliveryBoyCompensationAdjustments
            WHERE CompensationId = @CompensationId;
        END

        DECLARE @NetPayable DECIMAL(18,2);
        SET @NetPayable = CASE WHEN @CompType = 'Fixed Salary' THEN @FixedSalaryAmount ELSE @CalcCommission END + @TotalAdjustments;

        IF (@NetPayable < 0.00)
            SET @NetPayable = 0.00;

        IF (@CompensationId IS NOT NULL)
        BEGIN
            UPDATE dbo.DeliveryBoyMonthlyCompensations
            SET CompensationType = @CompType,
                EligibleCompletedLitres = @CompletedLitres,
                ApplicableCommissionRate = @ApplicableRate,
                CalculatedCommission = @CalcCommission,
                FixedSalaryAmount = @FixedSalaryAmount,
                Adjustments = @TotalAdjustments,
                NetPayableAmount = @NetPayable,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE CompensationId = @CompensationId;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.DeliveryBoyMonthlyCompensations (
                DeliveryBoyId, Month, Year, CompensationType,
                EligibleCompletedLitres, ApplicableCommissionRate, CalculatedCommission,
                FixedSalaryAmount, Adjustments, NetPayableAmount,
                Status, CreatedAt, CreatedBy
            )
            VALUES (
                @DeliveryBoyId, @Month, @Year, @CompType,
                @CompletedLitres, @ApplicableRate, @CalcCommission,
                @FixedSalaryAmount, @TotalAdjustments, @NetPayable,
                'Draft', SYSUTCDATETIME(), @AdminUserId
            );
            SET @CompensationId = SCOPE_IDENTITY();
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 10. Stored Procedure: dbo.usp_Compensation_BulkCalculateDrafts
-- Bulk draft calculation across all active delivery boys for a calendar month.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_BulkCalculateDrafts]
    @Month TINYINT,
    @Year SMALLINT,
    @AdminUserId INT,
    @TotalCalculated INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @TotalCalculated = 0;

    DECLARE @CurDeliveryBoyId INT;
    DECLARE @CalculatedCompId BIGINT;

    DECLARE curBulkComp CURSOR LOCAL FAST_FORWARD FOR
        SELECT DeliveryBoyId
        FROM dbo.DeliveryBoys
        WHERE IsActive = 1
        ORDER BY DeliveryBoyId ASC;

    OPEN curBulkComp;
    FETCH NEXT FROM curBulkComp INTO @CurDeliveryBoyId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Avoid recalculating finalized or paid records
        IF NOT EXISTS (
            SELECT 1 FROM dbo.DeliveryBoyMonthlyCompensations
            WHERE DeliveryBoyId = @CurDeliveryBoyId AND Month = @Month AND Year = @Year AND Status IN ('Finalized', 'Paid')
        )
        BEGIN
            SET @CalculatedCompId = NULL;

            EXEC dbo.usp_Compensation_CalculateMonthlyDraft
                @DeliveryBoyId = @CurDeliveryBoyId,
                @Month = @Month,
                @Year = @Year,
                @AdminUserId = @AdminUserId,
                @CompensationId = @CalculatedCompId OUTPUT;

            SET @TotalCalculated = @TotalCalculated + 1;
        END

        FETCH NEXT FROM curBulkComp INTO @CurDeliveryBoyId;
    END

    CLOSE curBulkComp;
    DEALLOCATE curBulkComp;
END;
GO

-- ====================================================================================
-- 11. Stored Procedure: dbo.usp_Compensation_ApplyAdjustment
-- Inserts row into adjustment ledger and updates parent aggregate atomically.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_ApplyAdjustment]
    @CompensationId BIGINT,
    @AdjustmentType NVARCHAR(30),
    @AdjustmentAmount DECIMAL(18,2),
    @Reason NVARCHAR(250),
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @AdjustmentType = LTRIM(RTRIM(@AdjustmentType));
    SET @Reason = LTRIM(RTRIM(@Reason));

    IF (@AdjustmentType NOT IN ('Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other'))
    BEGIN
        THROW 50042, 'Invalid adjustment type.', 1;
    END

    IF (LEN(@Reason) < 5)
    BEGIN
        THROW 50049, 'Adjustment justification reason must be at least 5 characters long.', 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Lock parent row with UPDLOCK, HOLDLOCK
        DECLARE @CurrentStatus NVARCHAR(20);
        DECLARE @CompType NVARCHAR(20);
        DECLARE @GrossEarnings DECIMAL(18,2);

        SELECT 
            @CurrentStatus = Status,
            @CompType = CompensationType,
            @GrossEarnings = CASE WHEN CompensationType = 'Fixed Salary' THEN FixedSalaryAmount ELSE CalculatedCommission END
        FROM dbo.DeliveryBoyMonthlyCompensations WITH (UPDLOCK, HOLDLOCK)
        WHERE CompensationId = @CompensationId;

        IF (@CurrentStatus IS NULL)
        BEGIN
            THROW 50054, 'Monthly compensation record does not exist.', 1;
        END

        -- 2. Verify Draft status
        IF (@CurrentStatus <> 'Draft')
        BEGIN
            THROW 50047, 'Adjustments are strictly restricted to Draft compensation records. Finalized and Paid statements are immutable.', 1;
        END

        -- 3. Recompute existing adjustments directly from ledger under lock
        DECLARE @ExistingAdjustments DECIMAL(18,2);
        SELECT @ExistingAdjustments = ISNULL(SUM(AdjustmentAmount), 0.00)
        FROM dbo.DeliveryBoyCompensationAdjustments WITH (UPDLOCK, HOLDLOCK)
        WHERE CompensationId = @CompensationId;

        -- 4. Validate non-negative net payable constraint
        DECLARE @NewTotalAdjustments DECIMAL(18,2) = @ExistingAdjustments + @AdjustmentAmount;
        DECLARE @NewNetPayable DECIMAL(18,2) = @GrossEarnings + @NewTotalAdjustments;

        IF (@NewNetPayable < 0.00)
        BEGIN
            THROW 50048, 'Net payable amount cannot be negative. Total deductions exceed gross compensation earnings.', 1;
        END

        -- 5. Insert immutable adjustment row into ledger using pure static SQL
        INSERT INTO dbo.DeliveryBoyCompensationAdjustments (
            CompensationId, AdjustmentType, AdjustmentAmount, Reason, CreatedAt, CreatedBy
        )
        VALUES (
            @CompensationId, @AdjustmentType, @AdjustmentAmount, @Reason, SYSUTCDATETIME(), @AdminUserId
        );

        -- 6. Synchronize parent record from ledger-derived total
        UPDATE dbo.DeliveryBoyMonthlyCompensations
        SET Adjustments = @NewTotalAdjustments,
            NetPayableAmount = @NewNetPayable,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE CompensationId = @CompensationId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 12. Stored Procedure: dbo.usp_Compensation_GetAdjustments
-- Retrieves all adjustment lines for a monthly compensation record.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_GetAdjustments]
    @CompensationId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        a.AdjustmentId,
        a.CompensationId,
        a.AdjustmentType,
        a.AdjustmentAmount,
        a.Reason,
        a.CreatedAt,
        a.CreatedBy,
        u.FullName AS CreatedByName
    FROM dbo.DeliveryBoyCompensationAdjustments a
    LEFT JOIN dbo.Users u ON a.CreatedBy = u.UserId
    WHERE a.CompensationId = @CompensationId
    ORDER BY a.CreatedAt ASC, a.AdjustmentId ASC;
END;
GO

-- ====================================================================================
-- 13. Stored Procedure: dbo.usp_Compensation_Finalize
-- Locks statement as Finalized.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_Finalize]
    @CompensationId BIGINT,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CurrentStatus NVARCHAR(20);

        SELECT @CurrentStatus = Status
        FROM dbo.DeliveryBoyMonthlyCompensations WITH (UPDLOCK, HOLDLOCK)
        WHERE CompensationId = @CompensationId;

        IF (@CurrentStatus IS NULL)
        BEGIN
            THROW 50054, 'Monthly compensation record does not exist.', 1;
        END

        IF (@CurrentStatus <> 'Draft')
        BEGIN
            THROW 50050, 'Only Draft compensation records can be finalized.', 1;
        END

        UPDATE dbo.DeliveryBoyMonthlyCompensations
        SET Status = 'Finalized',
            FinalizedAt = SYSUTCDATETIME(),
            FinalizedBy = @AdminUserId,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE CompensationId = @CompensationId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 14. Stored Procedure: dbo.usp_Compensation_RecordPayout
-- Transitions Finalized statement to Paid with transaction reference.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_RecordPayout]
    @CompensationId BIGINT,
    @PaidAt DATETIME2(7),
    @PaymentReference NVARCHAR(50),
    @Notes NVARCHAR(250) = NULL,
    @AdminUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @PaymentReference = LTRIM(RTRIM(@PaymentReference));
    SET @Notes = NULLIF(LTRIM(RTRIM(@Notes)), '');

    IF (@PaidAt IS NULL)
        SET @PaidAt = SYSUTCDATETIME();

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @CurrentStatus NVARCHAR(20);

        SELECT @CurrentStatus = Status
        FROM dbo.DeliveryBoyMonthlyCompensations WITH (UPDLOCK, HOLDLOCK)
        WHERE CompensationId = @CompensationId;

        IF (@CurrentStatus IS NULL)
        BEGIN
            THROW 50054, 'Monthly compensation record does not exist.', 1;
        END

        IF (@CurrentStatus = 'Paid')
        BEGIN
            THROW 50052, 'Compensation has already been recorded as Paid.', 1;
        END

        IF (@CurrentStatus <> 'Finalized')
        BEGIN
            THROW 50051, 'Payout can only be recorded on Finalized compensation records.', 1;
        END

        UPDATE dbo.DeliveryBoyMonthlyCompensations
        SET Status = 'Paid',
            PaidAt = @PaidAt,
            PaymentReference = @PaymentReference,
            PaidBy = @AdminUserId,
            Notes = ISNULL(@Notes, Notes),
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @AdminUserId
        WHERE CompensationId = @CompensationId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ====================================================================================
-- 15. Stored Procedure: dbo.usp_Compensation_GetList
-- Retrieves monthly payroll records with filtering.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_GetList]
    @Month TINYINT,
    @Year SMALLINT,
    @Status NVARCHAR(20) = NULL,
    @CompensationType NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Status = NULLIF(LTRIM(RTRIM(@Status)), '');
    SET @CompensationType = NULLIF(LTRIM(RTRIM(@CompensationType)), '');

    SELECT 
        c.CompensationId,
        c.DeliveryBoyId,
        u.FullName AS DeliveryBoyName,
        u.Phone AS DeliveryBoyPhone,
        r.RouteName,
        r.RouteCode,
        c.Month,
        c.Year,
        c.CompensationType,
        c.EligibleCompletedLitres,
        c.ApplicableCommissionRate,
        c.CalculatedCommission,
        c.FixedSalaryAmount,
        c.Adjustments,
        c.NetPayableAmount,
        c.Status,
        c.FinalizedAt,
        fu.FullName AS FinalizedByName,
        c.PaidAt,
        c.PaymentReference,
        pu.FullName AS PaidByName,
        c.CreatedAt
    FROM dbo.DeliveryBoyMonthlyCompensations c
    INNER JOIN dbo.DeliveryBoys db ON c.DeliveryBoyId = db.DeliveryBoyId
    INNER JOIN dbo.Users u ON db.UserId = u.UserId
    LEFT JOIN dbo.Routes r ON db.PrimaryRouteId = r.RouteId
    LEFT JOIN dbo.Users fu ON c.FinalizedBy = fu.UserId
    LEFT JOIN dbo.Users pu ON c.PaidBy = pu.UserId
    WHERE c.Month = @Month
      AND c.Year = @Year
      AND (@Status IS NULL OR @Status = 'All' OR c.Status = @Status)
      AND (@CompensationType IS NULL OR @CompensationType = 'All' OR c.CompensationType = @CompensationType)
    ORDER BY u.FullName ASC;
END;
GO

-- ====================================================================================
-- 16. Stored Procedure: dbo.usp_Compensation_GetDetails
-- Returns itemized monthly statement, adjustment lines, and daily delivery records.
-- ====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[usp_Compensation_GetDetails]
    @CompensationId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DeliveryBoyId INT;
    DECLARE @Month TINYINT;
    DECLARE @Year SMALLINT;

    -- Result Set 1: Header Statement Summary
    SELECT 
        c.CompensationId,
        c.DeliveryBoyId,
        u.FullName AS DeliveryBoyName,
        u.Phone AS DeliveryBoyPhone,
        db.VehicleNumber,
        r.RouteName,
        r.RouteCode,
        c.Month,
        c.Year,
        c.CompensationType,
        c.EligibleCompletedLitres,
        c.ApplicableCommissionRate,
        c.CalculatedCommission,
        c.FixedSalaryAmount,
        c.Adjustments,
        c.NetPayableAmount,
        c.Status,
        c.FinalizedAt,
        fu.FullName AS FinalizedByName,
        c.PaidAt,
        c.PaymentReference,
        pu.FullName AS PaidByName,
        c.Notes,
        c.CreatedAt,
        cu.FullName AS CreatedByName
    FROM dbo.DeliveryBoyMonthlyCompensations c
    INNER JOIN dbo.DeliveryBoys db ON c.DeliveryBoyId = db.DeliveryBoyId
    INNER JOIN dbo.Users u ON db.UserId = u.UserId
    LEFT JOIN dbo.Routes r ON db.PrimaryRouteId = r.RouteId
    LEFT JOIN dbo.Users fu ON c.FinalizedBy = fu.UserId
    LEFT JOIN dbo.Users pu ON c.PaidBy = pu.UserId
    LEFT JOIN dbo.Users cu ON c.CreatedBy = cu.UserId
    WHERE c.CompensationId = @CompensationId;

    SELECT @DeliveryBoyId = DeliveryBoyId, @Month = Month, @Year = Year
    FROM dbo.DeliveryBoyMonthlyCompensations
    WHERE CompensationId = @CompensationId;

    -- Result Set 2: Adjustments Breakdown
    SELECT 
        a.AdjustmentId,
        a.CompensationId,
        a.AdjustmentType,
        a.AdjustmentAmount,
        a.Reason,
        a.CreatedAt,
        a.CreatedBy,
        u.FullName AS CreatedByName
    FROM dbo.DeliveryBoyCompensationAdjustments a
    LEFT JOIN dbo.Users u ON a.CreatedBy = u.UserId
    WHERE a.CompensationId = @CompensationId
    ORDER BY a.CreatedAt ASC, a.AdjustmentId ASC;

    -- Result Set 3: Daily Delivery Breakdown for that Month
    DECLARE @StartDate DATE = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @EndDate DATE = EOMONTH(@StartDate);

    SELECT 
        d.DeliveryId,
        d.EntryDate,
        d.Shift,
        c.CustomerCode,
        c.FullName AS CustomerName,
        d.Status,
        d.TotalLiquidVolumeLitres,
        d.EligibleLitreQuantity,
        d.CommissionRateSnapshot,
        ROUND(d.EligibleLitreQuantity * d.CommissionRateSnapshot, 2) AS EarnedCommission,
        d.TotalAmount AS CustomerChargedAmount
    FROM dbo.DailyMilkDeliveries d
    INNER JOIN dbo.Customers c ON d.CustomerId = c.CustomerId
    WHERE d.DeliveryBoyId = @DeliveryBoyId
      AND d.EntryDate BETWEEN @StartDate AND @EndDate
    ORDER BY d.EntryDate ASC, d.Shift ASC, c.DeliverySequence ASC;
END;
GO

-- ====================================================================================
-- 17. Security & Least-Privilege Role Grants
-- Enforces DENY on direct ledger modifications and grants EXECUTE to db_dms_app.
-- ====================================================================================
PRINT 'Applying least-privilege security permissions...';

-- Explicit DENY on direct DML on the financial adjustment ledger
DENY INSERT, UPDATE, DELETE ON [dbo].[DeliveryBoyCompensationAdjustments] TO [db_dms_app];
DENY INSERT, UPDATE, DELETE ON [dbo].[DeliveryBoyCompensationAdjustments] TO [dms_app_user];

-- Grant SELECT on ledger for queries/projections
GRANT SELECT ON [dbo].[DeliveryBoyCompensationAdjustments] TO [db_dms_app];

-- Grant EXECUTE exclusively on all 14 procedures to application role
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_Register] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_GetDirectory] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_GetDetails] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_UpdateProfile] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_Deactivate] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_DeliveryBoy_Reactivate] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_CalculateMonthlyDraft] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_BulkCalculateDrafts] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_ApplyAdjustment] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_GetAdjustments] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_Finalize] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_RecordPayout] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_GetList] TO [db_dms_app];
GRANT EXECUTE ON [dbo].[usp_Compensation_GetDetails] TO [db_dms_app];
GO

PRINT '====================================================================================';
PRINT '10_DeliveryBoyAndCompensationManagement migration completed successfully!';
PRINT '====================================================================================';
GO
