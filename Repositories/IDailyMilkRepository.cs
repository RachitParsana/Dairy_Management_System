using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.DailyMilk;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Repository contract for Daily Milk delivery persistence operations against:
    /// dbo.DailyMilkDeliveries, dbo.DailyMilkDeliveryItems, dbo.DailyDeliveryBatches,
    /// dbo.DailyMilkDeliveryCorrections, dbo.CustomerUnscheduledDeliveryAuthorizations,
    /// dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions, and dbo.DeliveryBoyCommissionRates.
    /// Pure ADO.NET using Microsoft.Data.SqlClient.
    /// </summary>
    public interface IDailyMilkRepository
    {
        #region Runsheet & Continuous Scrolling Queries

        /// <summary>
        /// Retrieves the daily delivery runsheet roster for a delivery boy on a given date and shift.
        /// Includes assigned customers, active subscription defaults, custom rates, approved vacations,
        /// authorized unscheduled entries, and any existing draft/saved entries. Ordered by Customers.DeliverySequence.
        /// </summary>
        Task<IReadOnlyList<DailyMilkListItemViewModel>> GetRosterAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the delivery runsheet across a route or delivery boy from an administrative perspective.
        /// Allows Admin monitoring across all delivery boys and unassigned customers.
        /// </summary>
        Task<IReadOnlyList<DailyMilkListItemViewModel>> GetAdminRosterAsync(
            int? routeId,
            int? deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a delivery header and all its child category line items by surrogate DeliveryId.
        /// </summary>
        Task<DailyMilkEntry?> GetDeliveryByIdAsync(
            long deliveryId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a delivery entry for a specific customer, date, and shift.
        /// </summary>
        Task<DailyMilkEntry?> GetDeliveryByCustomerDateShiftAsync(
            int customerId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams delivery records using a deterministic composite cursor continuous scrolling evaluation.
        /// Continuation Predicate for reverse chronological order:
        /// WHERE (@CursorEntryDate IS NULL)
        ///    OR (d.EntryDate &lt; @CursorEntryDate)
        ///    OR (d.EntryDate = @CursorEntryDate AND ShiftRank(d.Shift) &lt; ShiftRank(@CursorShift))
        ///    OR (d.EntryDate = @CursorEntryDate AND d.Shift = @CursorShift AND d.DeliveryId &lt; @CursorDeliveryId)
        /// ORDER BY d.EntryDate DESC, ShiftRank(d.Shift) DESC, d.DeliveryId DESC.
        /// Strictly eliminates PageNumber/PageSize pagination.
        /// </summary>
        Task<DailyMilkContinuousScrollResult<DailyMilkListItemViewModel>> GetDeliveriesContinuousAsync(
            DailyMilkFilterCriteria criteria,
            CancellationToken cancellationToken = default);

        #endregion

        #region Batch & Runsheet Persistence

        /// <summary>
        /// Retrieves batch metadata for a delivery boy on a specific date and shift.
        /// </summary>
        Task<DailyMilkBatch?> GetBatchAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves batch metadata by primary key BatchId.
        /// </summary>
        Task<DailyMilkBatch?> GetBatchByIdAsync(
            long batchId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically saves or updates a daily delivery batch header and all customer delivery entries with child items.
        /// Executed within an explicit SqlTransaction.
        /// Handles draft saves (Status = 'Draft') and final submissions (Status = 'Submitted').
        /// Note: Submitting a batch preserves any remaining 'Pending' entries as 'Pending'; they are NOT automatically converted.
        /// Validates and records unscheduled delivery authorization consumptions inside the transaction.
        /// </summary>
        Task<long> SaveBatchAsync(
            DailyMilkBatch batch,
            IReadOnlyList<DailyMilkEntry> entries,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves or updates an individual customer delivery record and its child items within a transaction.
        /// </summary>
        Task<long> SaveDeliveryEntryAsync(
            DailyMilkEntry entry,
            CancellationToken cancellationToken = default);

        #endregion

        #region Admin Corrections & Audit Trail

        /// <summary>
        /// Atomically executes an administrative correction on an existing delivery record.
        /// Captures complete before-and-after values into dbo.DailyMilkDeliveryCorrections,
        /// updates dbo.DailyMilkDeliveries and dbo.DailyMilkDeliveryItems,
        /// and recalculates header totals and eligible litres.
        /// Blocked if the month's bill is already published.
        /// </summary>
        Task CorrectDeliveryEntryAsync(
            long deliveryId,
            string newStatus,
            string? nonDeliveryReason,
            IReadOnlyList<DailyMilkCategoryEntry> updatedItems,
            string correctionReason,
            int adminUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the historical correction log for a specific delivery record.
        /// </summary>
        Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetCorrectionHistoryAsync(
            long deliveryId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves recent administrative corrections across the system for audit review.
        /// </summary>
        Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetRecentCorrectionsAsync(
            int count,
            CancellationToken cancellationToken = default);

        #endregion

        #region Unscheduled Delivery Authorization Management

        /// <summary>
        /// Creates an explicit unscheduled delivery authorization record (Morning, Evening, or Both).
        /// Strictly executed by an Administrator.
        /// </summary>
        Task<long> CreateUnscheduledAuthorizationAsync(
            UnscheduledDeliveryAuthorization authorization,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves an active, non-revoked authorization for a customer, date, and shift that has not yet been
        /// consumed for the requested shift.
        /// </summary>
        Task<UnscheduledDeliveryAuthorization?> GetActiveUnscheduledAuthorizationAsync(
            int customerId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records an explicit shift-specific consumption event in dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions.
        /// Protected by UQ_UnscheduledAuth_Consumption_Shift to prevent duplicate consumption for the same shift.
        /// </summary>
        Task RecordAuthorizationConsumptionAsync(
            long authorizationId,
            long deliveryId,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes a previously granted unscheduled delivery authorization prior to consumption.
        /// </summary>
        Task RevokeUnscheduledAuthorizationAsync(
            long authorizationId,
            string revokeReason,
            int adminUserId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Deterministic Pricing, Effective Commission & Guard Checks

        /// <summary>
        /// Resolves the applicable unit price for a customer, category, and date.
        /// Checks active CustomerCustomPricing first, falling back to MilkCategories.DefaultPrice.
        /// Returns null if no active price exists.
        /// </summary>
        Task<decimal?> ResolveUnitPriceAsync(
            int customerId,
            int categoryId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the delivery boy's effective per-litre commission rate on EntryDate.
        /// Resolves strictly from effective-dated history in dbo.DeliveryBoyCommissionRates:
        /// WHERE DeliveryBoyId = @DeliveryBoyId AND EffectiveFrom &lt;= @EntryDate AND (EffectiveTo IS NULL OR EffectiveTo &gt;= @EntryDate).
        /// Returns 0.00 for salaried staff (CompensationType == 'Fixed Salary') without requiring a commission rate.
        /// Returns null if the delivery boy is commission-based but has no verified rate covering EntryDate.
        /// Zero silent fallback: Never falls back to CurrentCommissionRate or fabricates historical rates.
        /// If null is returned during final submission, the operation must fail with an explicit validation error.
        /// </summary>
        Task<decimal?> ResolveCommissionRateAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the effective-dated commission rate history for a delivery boy ordered by EffectiveFrom DESC.
        /// </summary>
        Task<IReadOnlyList<DeliveryBoyCommissionRate>> GetCommissionRateHistoryAsync(
            int deliveryBoyId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Configures a new effective-dated commission rate tier for a delivery boy via authorized stored procedure.
        /// Enforces strict transaction ordering: begins a transaction, collects all affected DeliveryBoyIds (including old
        /// and new for reassignment), and acquires UPDLOCK/HOLDLOCK on parent DeliveryBoys rows in deterministic ascending order
        /// BEFORE any rate-table modification or overlap validation. Closes prior open-ended rate tiers.
        /// The AFTER INSERT, UPDATE trigger remains an independent defensive database-level validation guard.
        /// If SQL Server detects a deadlock (error 1205), the entire transaction is rolled back and retried from the beginning;
        /// retries reacquire locks, repeat all validations, and never bypass trigger or overlap checks.
        /// </summary>
        Task<int> CreateCommissionRateAsync(
            DeliveryBoyCommissionRate rate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a monthly bill containing this delivery date has already been published.
        /// Used as a hard guard to prevent mutating daily deliveries for closed billing periods.
        /// </summary>
        Task<bool> IsDeliveryDateBilledAndPublishedAsync(
            int customerId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves aggregate delivery statistics (completed, pending, vacation, litres, amounts) for a date and shift.
        /// </summary>
        Task<DailyMilkSummaryViewModel> GetDailyShiftSummaryAsync(
            DateOnly entryDate,
            string shift,
            int? deliveryBoyId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Computes total confirmed eligible liquid litres delivered by a delivery boy during a calendar month.
        /// Evaluates completed liquid deliveries excluding any overlapping approved vacations.
        /// Used directly by delivery boy commission calculations.
        /// </summary>
        Task<decimal> GetMonthlyDeliveredLitresAsync(
            int deliveryBoyId,
            int month,
            int year,
            CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// Sibling repository contract for Administrator batch lock/unlock management.
    /// Kept separate from IDailyMilkRepository so existing test doubles remain compatible.
    /// </summary>
    public interface IDailyMilkBatchAdminRepository
    {
        Task<IReadOnlyList<DailyMilkBatchAdminItemDto>> GetBatchesForAdminAsync(
            DateOnly? entryDate,
            string? shift,
            int? deliveryBoyId,
            string? status = null,
            int limit = 50,
            CancellationToken cancellationToken = default);

        Task UnlockBatchAsync(
            long batchId,
            string unlockReason,
            int adminUserId,
            CancellationToken cancellationToken = default);
    }
}
