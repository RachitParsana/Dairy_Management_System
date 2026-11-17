-- ====================================================================================
-- Script 05: Core Stored Procedures
-- Project: Dairy Management System (DMS)
-- Database: DairyManagementDb
-- Section: 10.1 Key Transactional Boundaries & 11. ADO.NET Data Access Mapping
-- Author: Senior SQL Server Database Architect & ADO.NET Backend Engineer
-- Description: Implements concurrency-safe payment allocation procedure using
--              sequential ascending lock acquisition on Table-Valued Parameters,
--              Indian Standard Time overdue evaluation, and strict atomicity.
-- ====================================================================================

USE [DairyManagementDb];
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_AllocatePayment]
    @PaymentId BIGINT,
    @AdminUserId INT,
    @Allocations [dbo].[PaymentAllocationItemType] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @InLocalTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        BEGIN TRANSACTION;
        SET @InLocalTransaction = 1;
    END

    DECLARE @CursorOpen BIT = 0;

    BEGIN TRY
        -- 1. Validate Admin User existence
        IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE UserId = @AdminUserId AND IsActive = 1)
        BEGIN
            THROW 50010, 'Invalid or inactive administrative user.', 1;
        END

        -- 2. Lock parent Payment receipt row
        DECLARE @CustomerId INT;
        DECLARE @PaymentAmount DECIMAL(18,2);
        DECLARE @IsVoided BIT;

        SELECT
            @CustomerId = CustomerId,
            @PaymentAmount = Amount,
            @IsVoided = IsVoided
        FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE PaymentId = @PaymentId;

        IF @CustomerId IS NULL
        BEGIN
            THROW 50006, 'Target payment receipt does not exist.', 1;
        END

        IF @IsVoided = 1
        BEGIN
            THROW 50002, 'Cannot allocate a voided payment receipt.', 1;
        END

        -- 3. Calculate total allocation in payload
        DECLARE @TotalNewAllocation DECIMAL(18,2) = ISNULL((SELECT SUM(AllocatedAmount) FROM @Allocations), 0.00);

        IF @TotalNewAllocation <= 0.00
        BEGIN
            THROW 50007, 'Total allocation amount must be strictly greater than zero.', 1;
        END

        -- 4. Check available unallocated capacity on payment receipt
        DECLARE @AlreadyAllocated DECIMAL(18,2) = ISNULL(
            (SELECT SUM(AllocatedAmount)
             FROM dbo.PaymentAllocations WITH (UPDLOCK, HOLDLOCK)
             WHERE PaymentId = @PaymentId AND IsVoided = 0), 0.00);

        IF (@AlreadyAllocated + @TotalNewAllocation > @PaymentAmount)
        BEGIN
            THROW 50003, 'Total active allocations exceed payment receipt amount.', 1;
        END

        -- 5. Sequential ascending lock acquisition across target bills
        DECLARE @CurrentBillId BIGINT;
        DECLARE @CurrentAllocAmount DECIMAL(18,2);
        DECLARE @CurrentCustomerId INT;
        DECLARE @CurrentBalance DECIMAL(18,2);
        DECLARE @CurrentPaid DECIMAL(18,2);
        DECLARE @CurrentPeriodEnd DATE;

        DECLARE bill_alloc_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT BillId, AllocatedAmount
            FROM @Allocations
            ORDER BY BillId ASC;

        OPEN bill_alloc_cursor;
        SET @CursorOpen = 1;

        FETCH NEXT FROM bill_alloc_cursor INTO @CurrentBillId, @CurrentAllocAmount;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Acquire single-row lock in ascending primary key order
            SELECT
                @CurrentCustomerId = CustomerId,
                @CurrentBalance = BalanceAmount,
                @CurrentPaid = PaidAmount,
                @CurrentPeriodEnd = PeriodEndDate
            FROM dbo.MonthlyBills WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE BillId = @CurrentBillId;

            IF @@ROWCOUNT = 0
            BEGIN
                THROW 50005, 'Target monthly bill does not exist.', 1;
            END

            -- Validate customer ownership (cross-customer allocation prohibited)
            IF (@CurrentCustomerId <> @CustomerId)
            BEGIN
                THROW 50001, 'Cross-customer allocation prohibited. Bill does not belong to paying customer.', 1;
            END

            -- Validate individual bill balance upper bound
            IF (@CurrentAllocAmount > @CurrentBalance)
            BEGIN
                THROW 50004, 'Allocated amount exceeds bill outstanding balance.', 1;
            END

            -- Insert or update PaymentAllocations
            IF EXISTS (SELECT 1 FROM dbo.PaymentAllocations WITH (UPDLOCK, HOLDLOCK) WHERE PaymentId = @PaymentId AND BillId = @CurrentBillId)
            BEGIN
                UPDATE dbo.PaymentAllocations
                SET AllocatedAmount = AllocatedAmount + @CurrentAllocAmount,
                    IsVoided = 0,
                    AllocatedAt = SYSUTCDATETIME(),
                    AllocatedBy = @AdminUserId
                WHERE PaymentId = @PaymentId AND BillId = @CurrentBillId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.PaymentAllocations (PaymentId, BillId, AllocatedAmount, IsVoided, AllocatedAt, AllocatedBy)
                VALUES (@PaymentId, @CurrentBillId, @CurrentAllocAmount, 0, SYSUTCDATETIME(), @AdminUserId);
            END

            -- Update bill balances and payment status using authoritative Indian business date
            UPDATE dbo.MonthlyBills
            SET
                PaidAmount = PaidAmount + @CurrentAllocAmount,
                BalanceAmount = BalanceAmount - @CurrentAllocAmount,
                PaymentStatus = CASE
                    WHEN (BalanceAmount - @CurrentAllocAmount) = 0.00 THEN 'Paid'
                    WHEN (BalanceAmount - @CurrentAllocAmount) > 0.00
                         AND CONVERT(date, SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time') > PeriodEndDate THEN 'Overdue'
                    WHEN (PaidAmount + @CurrentAllocAmount) > 0.00
                         AND (BalanceAmount - @CurrentAllocAmount) > 0.00
                         AND CONVERT(date, SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time') <= PeriodEndDate THEN 'Partially Paid'
                    ELSE 'Unpaid'
                END,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @AdminUserId
            WHERE BillId = @CurrentBillId;

            FETCH NEXT FROM bill_alloc_cursor INTO @CurrentBillId, @CurrentAllocAmount;
        END

        CLOSE bill_alloc_cursor;
        DEALLOCATE bill_alloc_cursor;
        SET @CursorOpen = 0;

        IF @InLocalTransaction = 1 AND @@TRANCOUNT > 0
        BEGIN
            COMMIT TRANSACTION;
        END
    END TRY
    BEGIN CATCH
        IF @CursorOpen = 1
        BEGIN
            CLOSE bill_alloc_cursor;
            DEALLOCATE bill_alloc_cursor;
        END

        IF @InLocalTransaction = 1 AND @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH
END
GO

PRINT 'Stored procedure dbo.usp_AllocatePayment created successfully.';
GO
