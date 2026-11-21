using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Billing;
using Dairy_Management_System.Repositories;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Production-grade application service orchestrating business rules, input validations,
    /// and ledger transactions for Day 5 Billing & Payments Management.
    /// </summary>
    public class BillingService : IBillingService
    {
        private readonly IBillingRepository _billingRepository;
        private readonly ILogger<BillingService> _logger;
        private readonly INotificationService? _notificationService;

        public BillingService(
            IBillingRepository billingRepository,
            ILogger<BillingService> logger,
            INotificationService? notificationService = null)
        {
            _billingRepository = billingRepository ?? throw new ArgumentNullException(nameof(billingRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService;
        }

        #region Invoice Operations

        public async Task<long> GenerateCustomerBillAsync(int customerId, int month, int year, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (customerId <= 0)
                throw new ArgumentException("Valid Customer ID is required.", nameof(customerId));

            ValidateMonthYear(month, year);

            _logger.LogInformation("Generating monthly bill for CustomerId: {CustomerId}, Period: {Month}/{Year} by AdminUserId: {AdminUserId}",
                customerId, month, year, adminUserId);

            var billId = await _billingRepository.GenerateCustomerBillAsync(customerId, month, year, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Successfully generated BillId: {BillId} for CustomerId: {CustomerId}", billId, customerId);
            return billId;
        }

        public async Task<MonthlyBill?> GetBillDetailsAsync(long billId, CancellationToken cancellationToken = default)
        {
            if (billId <= 0)
                throw new ArgumentException("Valid Bill ID is required.", nameof(billId));

            return await _billingRepository.GetBillByIdAsync(billId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<(IReadOnlyList<MonthlyBill> Bills, int TotalCount)> GetBillsContinuousAsync(
            MonthlyBillFilterCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            criteria ??= new MonthlyBillFilterCriteria();

            var countTask = _billingRepository.GetBillsCountAsync(criteria, cancellationToken);
            var billsTask = _billingRepository.GetBillsAsync(criteria, cancellationToken);

            await Task.WhenAll(countTask, billsTask).ConfigureAwait(false);

            return (await billsTask, await countTask);
        }

        public async Task<bool> PublishBillAsync(long billId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (billId <= 0)
                throw new ArgumentException("Valid Bill ID is required.", nameof(billId));

            if (graceDays.HasValue && graceDays.Value < 0)
                throw new ArgumentException("Grace days cannot be negative.", nameof(graceDays));

            _logger.LogInformation("Publishing BillId: {BillId} with GraceDays: {GraceDays} by AdminUserId: {AdminUserId}",
                billId, graceDays, adminUserId);

            var result = await _billingRepository.PublishBillAsync(billId, graceDays, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            if (result && _notificationService != null)
            {
                const int maxRetries = 3;
                bool emitted = false;
                for (int attempt = 1; attempt <= maxRetries && !emitted; attempt++)
                {
                    try
                    {
                        await _notificationService.EmitBillPublishedNotificationAsync(billId, adminUserId, cancellationToken)
                            .ConfigureAwait(false);
                        emitted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} to emit notification for BillId: {BillId} failed.", attempt, maxRetries, billId);
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(50 * attempt, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogError(ex, "Notification emission failed for published BillId: {BillId} after {MaxRetries} retries. Triggering durable reconciliation.", billId, maxRetries);
                            try
                            {
                                await _notificationService.ReconcilePublishedBillNotificationsAsync(adminUserId, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception reconEx)
                            {
                                _logger.LogError(reconEx, "Reconciliation attempt for BillId: {BillId} encountered error. Bill remains published safely.", billId);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("BillId: {BillId} published successfully.", billId);
            return result;
        }

        public async Task<int> BulkPublishBillsAsync(int month, int year, int? routeId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default)
        {
            ValidateMonthYear(month, year);

            if (graceDays.HasValue && graceDays.Value < 0)
                throw new ArgumentException("Grace days cannot be negative.", nameof(graceDays));

            _logger.LogInformation("Executing bulk publish for Month: {Month}/{Year}, RouteId: {RouteId}, GraceDays: {GraceDays}",
                month, year, routeId, graceDays);

            var count = await _billingRepository.BulkPublishBillsAsync(month, year, routeId, graceDays, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            if (count > 0 && _notificationService != null)
            {
                try
                {
                    var reconciled = await _notificationService.ReconcilePublishedBillNotificationsAsync(adminUserId, cancellationToken)
                        .ConfigureAwait(false);
                    _logger.LogInformation("Reconciled {Reconciled} notifications for bulk published bills.", reconciled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reconcile notifications for bulk published bills.");
                }
            }

            _logger.LogInformation("Bulk publish complete. Total bills published: {Count}", count);
            return count;
        }

        public async Task<bool> ReviseBillAsync(ReviseBillViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.BillId <= 0)
                throw new ArgumentException("Valid Bill ID is required.", nameof(model.BillId));

            if (model.ExpectedVersion <= 0)
                throw new ArgumentException("Expected version must be positive.", nameof(model.ExpectedVersion));

            if (model.Adjustments < 0)
                throw new ArgumentException("Adjustments cannot be negative.", nameof(model.Adjustments));

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
                throw new ArgumentException("Revision justification reason must be at least 5 characters.", nameof(model.Reason));

            _logger.LogInformation("Revising BillId: {BillId}, ExpectedVersion: {ExpectedVersion}, Adjustments: {Adjustments}, Reason: {Reason} by AdminUserId: {AdminUserId}",
                model.BillId, model.ExpectedVersion, model.Adjustments, model.Reason, adminUserId);

            var result = await _billingRepository.ReviseBillAsync(model, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("BillId: {BillId} revised successfully.", model.BillId);
            return result;
        }

        #endregion

        #region Bulk Billing

        public async Task<IReadOnlyList<CustomerBillingEligibilityModel>> GetEligibleCustomersForBillingAsync(
            int month,
            int year,
            int? routeId,
            CancellationToken cancellationToken = default)
        {
            ValidateMonthYear(month, year);

            return await _billingRepository.GetEligibleCustomersForBillingAsync(month, year, routeId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<BulkBillingResultModel> GenerateBulkMonthlyBillsAsync(
            int month,
            int year,
            int? routeId,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            ValidateMonthYear(month, year);

            _logger.LogInformation("Starting bulk monthly billing generation for Month: {Month}/{Year}, RouteId: {RouteId} by AdminUserId: {AdminUserId}",
                month, year, routeId, adminUserId);

            var result = await _billingRepository.GenerateBulkMonthlyBillsAsync(month, year, routeId, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Bulk billing run completed: Eligible: {Eligible}, Succeeded: {Succeeded}, Skipped: {Skipped}, Failed: {Failed}",
                result.TotalEligible, result.TotalSucceeded, result.TotalSkipped, result.TotalFailed);

            return result;
        }

        #endregion

        #region Payments & Ledger

        public async Task<long> RecordAndAllocateSinglePaymentAsync(RecordSinglePaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.CustomerId <= 0)
                throw new ArgumentException("Valid Customer ID is required.", nameof(model.CustomerId));

            if (model.Amount <= 0)
                throw new ArgumentException("Payment amount must be greater than zero.", nameof(model.Amount));

            if (model.PaymentMode != "Online" && model.PaymentMode != "Offline")
                throw new ArgumentException("Payment mode must be strictly 'Online' or 'Offline'.", nameof(model.PaymentMode));

            if (model.TargetBillId <= 0)
                throw new ArgumentException("Valid target bill must be selected.", nameof(model.TargetBillId));

            _logger.LogInformation("Recording manual payment receipt of ₹{Amount} ({Mode}) for CustomerId: {CustomerId}, TargetBillId: {TargetBillId} by AdminUserId: {AdminUserId}",
                model.Amount, model.PaymentMode, model.CustomerId, model.TargetBillId, adminUserId);

            var paymentId = await _billingRepository.RecordAndAllocateSinglePaymentAsync(model, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Successfully recorded PaymentId: {PaymentId} for CustomerId: {CustomerId}", paymentId, model.CustomerId);
            return paymentId;
        }

        public async Task<bool> AllocateAdvanceCreditSingleAsync(AllocateAdvanceCreditViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.PaymentId <= 0)
                throw new ArgumentException("Valid Payment ID is required.", nameof(model.PaymentId));

            if (model.TargetBillId <= 0)
                throw new ArgumentException("Valid target bill must be selected.", nameof(model.TargetBillId));

            if (model.Amount <= 0)
                throw new ArgumentException("Allocation amount must be greater than zero.", nameof(model.Amount));

            _logger.LogInformation("Allocating advance credit of ₹{Amount} from PaymentId: {PaymentId} to BillId: {BillId} by AdminUserId: {AdminUserId}",
                model.Amount, model.PaymentId, model.TargetBillId, adminUserId);

            var result = await _billingRepository.AllocateAdvanceCreditSingleAsync(
                model.PaymentId,
                model.TargetBillId,
                model.Amount,
                model.Reason,
                adminUserId,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Advance credit allocated successfully.");
            return result;
        }

        public async Task<bool> VoidPaymentAsync(VoidPaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.PaymentId <= 0)
                throw new ArgumentException("Valid Payment ID is required.", nameof(model.PaymentId));

            if (string.IsNullOrWhiteSpace(model.VoidReason) || model.VoidReason.Trim().Length < 10)
                throw new ArgumentException("Void justification reason is mandatory and must be at least 10 characters.", nameof(model.VoidReason));

            _logger.LogWarning("VOIDING PaymentId: {PaymentId} with Reason: '{Reason}' by AdminUserId: {AdminUserId}",
                model.PaymentId, model.VoidReason, adminUserId);

            var result = await _billingRepository.VoidPaymentAsync(model.PaymentId, model.VoidReason, adminUserId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("PaymentId: {PaymentId} voided successfully.", model.PaymentId);
            return result;
        }

        public async Task<Payment?> GetPaymentDetailsAsync(long paymentId, CancellationToken cancellationToken = default)
        {
            if (paymentId <= 0)
                throw new ArgumentException("Valid Payment ID is required.", nameof(paymentId));

            return await _billingRepository.GetPaymentByIdAsync(paymentId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<(IReadOnlyList<Payment> Payments, int TotalCount)> GetPaymentsHistoryAsync(
            PaymentFilterCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            criteria ??= new PaymentFilterCriteria();

            var countTask = _billingRepository.GetPaymentsCountAsync(criteria, cancellationToken);
            var paymentsTask = _billingRepository.GetPaymentsAsync(criteria, cancellationToken);

            await Task.WhenAll(countTask, paymentsTask).ConfigureAwait(false);

            return (await paymentsTask, await countTask);
        }

        public async Task<IReadOnlyList<CustomerOutstandingBillItemViewModel>> GetCustomerOutstandingBillsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            if (customerId <= 0)
                throw new ArgumentException("Valid Customer ID is required.", nameof(customerId));

            return await _billingRepository.GetCustomerOutstandingBillsAsync(customerId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<CustomerBalanceSummaryModel> GetCustomerBalanceSummaryAsync(int customerId, CancellationToken cancellationToken = default)
        {
            if (customerId <= 0)
                throw new ArgumentException("Valid Customer ID is required.", nameof(customerId));

            return await _billingRepository.GetCustomerBalanceSummaryAsync(customerId, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion

        #region Reports

        public async Task<BillingSummaryReportModel> GetMonthlyBillingSummaryReportAsync(int month, int year, CancellationToken cancellationToken = default)
        {
            ValidateMonthYear(month, year);

            return await _billingRepository.GetMonthlyBillingSummaryReportAsync(month, year, cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion

        #region Private Validation Helpers

        private static void ValidateMonthYear(int month, int year)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

            if (year < 2000 || year > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2000 and 2100.");
        }

        #endregion
    }
}
