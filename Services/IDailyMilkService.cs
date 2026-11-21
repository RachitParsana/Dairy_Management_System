using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.DailyMilk;
using Dairy_Management_System.Models.DeliveryBoy;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract orchestrating business logic for the Daily Milk Management module.
    /// Enforces vacation gatekeeping, customer-to-delivery-boy boundary verification,
    /// deterministic price resolution, unscheduled delivery authorization workflows with multi-shift consumption,
    /// continuous scrolling data retrieval with composite cursor continuation, effective-dated commission tracking,
    /// and immutable audit logging.
    /// </summary>
    public interface IDailyMilkService
    {
        #region Runsheet & Continuous Scrolling Operations

        /// <summary>
        /// Retrieves the complete runsheet roster for the logged-in Delivery Boy on the selected date and shift.
        /// Resolves the user's DeliveryBoyId, validates assigned customers, applies approved vacation lockouts,
        /// incorporates active unscheduled authorizations, and pre-populates default subscription lines.
        /// </summary>
        Task<DailyMilkRosterViewModel> GetDeliveryRosterAsync(
            int deliveryBoyUserId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the runsheet roster across any route or delivery boy from an administrative perspective.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<DailyMilkRosterViewModel> GetAdminDeliveryRosterAsync(
            int? routeId,
            int? deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves detailed delivery information, itemized category lines, and correction audit logs for a delivery.
        /// </summary>
        Task<DailyMilkEntryViewModel?> GetDeliveryDetailsAsync(
            long deliveryId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries daily delivery entries using a deterministic composite cursor continuous scrolling feed.
        /// Evaluates lexicographic continuation across EntryDate, Shift, and DeliveryId in reverse chronological order.
        /// Strictly eliminates conventional PageNumber/PageSize pagination.
        /// </summary>
        Task<DailyMilkContinuousScrollResult<DailyMilkListItemViewModel>> GetDeliveriesContinuousAsync(
            DailyMilkFilterCriteria criteria,
            CancellationToken cancellationToken = default);

        #endregion

        #region Draft & Shift Submission

        /// <summary>
        /// Saves intermediate delivery entries as a Draft runsheet without locking the shift.
        /// Allows delivery boys to record progress while on the route and resume later.
        /// Preserves 'Pending' statuses. Does not freeze commission rates.
        /// </summary>
        Task<DailyMilkOperationResult> SaveDraftAsync(
            DailyMilkSubmissionViewModel model,
            int deliveryBoyUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finalizes and submits the shift delivery batch for the logged-in Delivery Boy.
        /// Validates assigned customers and strictly enforces approved vacation lockouts.
        /// Submitting a batch preserves any remaining 'Pending' entries as 'Pending'; they are NOT automatically converted.
        /// Only 'Completed' entries generate milk charges and commission.
        /// Verifies and consumes active unscheduled delivery authorizations inside the atomic transaction,
        /// supporting independent shift consumption for 'Both' authorizations.
        /// Resolves effective commission rate strictly from dbo.DeliveryBoyCommissionRates (EffectiveFrom &lt;= EntryDate &lt;= EffectiveTo).
        /// If the delivery boy is commission-based and no verified rate tier covers EntryDate, final submission fails with
        /// an actionable validation error (never silently falling back or fabricating rates).
        /// Fixed Salary employees receive 0.00 commission without requiring a commission rate.
        /// Captures immutable commission rate snapshots and locks the batch.
        /// </summary>
        Task<DailyMilkOperationResult> SubmitDailyDeliveriesAsync(
            DailyMilkSubmissionViewModel model,
            int deliveryBoyUserId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Administrative Corrections & Unscheduled Authorizations

        /// <summary>
        /// Authorizes and executes an administrative correction on an existing delivery entry.
        /// Strictly authorized for Admin users.
        /// Verifies that the delivery month is not already locked by a published bill.
        /// Captures an immutable before-and-after audit record with mandatory business justification.
        /// </summary>
        Task<DailyMilkOperationResult> CorrectDeliveryAsync(
            DailyMilkCorrectionViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Explicitly authorizes an unscheduled delivery exception for a customer on an off-schedule calendar date.
        /// Supports 'Morning', 'Evening', or 'Both' shift scopes.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<DailyMilkOperationResult> AuthorizeUnscheduledDeliveryAsync(
            CreateUnscheduledAuthorizationViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes a previously granted unscheduled delivery authorization prior to consumption.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<DailyMilkOperationResult> RevokeUnscheduledAuthorizationAsync(
            long authorizationId,
            string revokeReason,
            int adminUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the full administrative correction audit log for a delivery record.
        /// </summary>
        Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetCorrectionHistoryAsync(
            long deliveryId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves recent administrative corrections across the dairy operation for executive review.
        /// </summary>
        Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetRecentCorrectionsAsync(
            int count = 50,
            CancellationToken cancellationToken = default);

        #endregion

        #region Effective-Dated Commission Rate Management

        /// <summary>
        /// Retrieves the effective-dated commission rate history for a delivery boy.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<IReadOnlyList<DeliveryBoyCommissionRate>> GetCommissionRateHistoryAsync(
            int deliveryBoyId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Explicitly initializes the baseline commission rate for an existing delivery boy who does not yet have
        /// verified historical rate records. Requires an explicit verified EffectiveFrom date supplied by the Admin.
        /// Executes via authorized stored procedure enforcing parent lock acquisition in ascending order before rate-table DML.
        /// Never defaults to today and never backfills fabricated rates.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<DailyMilkOperationResult> InitializeCommissionRateAsync(
            InitializeDeliveryBoyCommissionRateViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new effective-dated commission rate tier for a delivery boy via authorized stored procedure.
        /// Enforces strict transaction ordering: acquires parent DeliveryBoys locks in deterministic ascending order
        /// BEFORE modifying rate records or validating date ranges, closing the prior open-ended tier.
        /// Defended by an independent AFTER trigger as a secondary validation guard.
        /// On deadlock (SqlException 1205), retries the entire operation from the start without bypassing validation.
        /// Strictly authorized for Admin users.
        /// </summary>
        Task<DailyMilkOperationResult> AddCommissionRateAsync(
            CreateDeliveryBoyCommissionRateViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default);

        #endregion

        #region Pricing, Commission & Summary Inquiries

        /// <summary>
        /// Deterministically resolves the unit price for a customer, category, and entry date.
        /// Checks CustomerCustomPricing first, then MilkCategories.DefaultPrice.
        /// Throws a validation exception if no positive price is configured.
        /// </summary>
        Task<decimal> GetEffectiveUnitPriceAsync(
            int customerId,
            int categoryId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves daily shift summary metrics (completion %, volume delivered, eligible litres, gross amount).
        /// </summary>
        Task<DailyMilkSummaryViewModel> GetDailySummaryAsync(
            DateOnly entryDate,
            string shift,
            int? deliveryBoyId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Day 7 Integration: Retrieves the active customer roster assigned to the Delivery Boy resolved from UserId,
        /// including subscription defaults, sequence, and today's vacation status.
        /// </summary>
        Task<IReadOnlyList<DeliveryBoyCustomerRosterItemViewModel>> GetAssignedCustomersRosterAsync(
            int userId,
            CancellationToken cancellationToken = default);

        #endregion
    }
}
