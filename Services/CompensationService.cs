using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Compensation;
using Dairy_Management_System.Repositories;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Production-grade application service orchestrating business rules, adjustment ledger integrity,
    /// and payroll lifecycles for Delivery Boy Monthly Compensation & Payroll.
    /// </summary>
    public class CompensationService : ICompensationService
    {
        private readonly ICompensationRepository _compensationRepository;
        private readonly ILogger<CompensationService> _logger;

        private static readonly HashSet<string> ValidAdjustmentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Bonus",
            "AdvanceDeduction",
            "PenaltyDeduction",
            "TransitionAdjustment",
            "Other"
        };

        public CompensationService(
            ICompensationRepository compensationRepository,
            ILogger<CompensationService> logger)
        {
            _compensationRepository = compensationRepository ?? throw new ArgumentNullException(nameof(compensationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MonthlyCompensationListItemDto>> GetCompensationsAsync(
            int month, int year, string? status, string? compensationType, CancellationToken ct = default)
        {
            ValidateMonthYear(month, year);
            return await _compensationRepository.GetCompensationsAsync(month, year, status, compensationType, ct).ConfigureAwait(false);
        }

        public async Task<MonthlyCompensationDetailsDto?> GetCompensationDetailsAsync(long compensationId, CancellationToken ct = default)
        {
            if (compensationId <= 0)
                throw new ArgumentException("Valid Compensation ID is required.", nameof(compensationId));

            return await _compensationRepository.GetCompensationDetailsAsync(compensationId, ct).ConfigureAwait(false);
        }

        public async Task<long> CalculateOrUpdateDraftAsync(int deliveryBoyId, int month, int year, int adminUserId, CancellationToken ct = default)
        {
            if (deliveryBoyId <= 0)
                throw new ArgumentException("Valid Delivery Boy ID is required.", nameof(deliveryBoyId));

            ValidateMonthYear(month, year);

            _logger.LogInformation("Calculating/updating monthly draft compensation for DeliveryBoyId {DeliveryBoyId}, {Month}/{Year} by Admin {AdminUserId}",
                deliveryBoyId, month, year, adminUserId);

            return await _compensationRepository.CalculateOrUpdateDraftAsync(deliveryBoyId, month, year, adminUserId, ct).ConfigureAwait(false);
        }

        public async Task<int> BulkCalculateDraftsAsync(int month, int year, int adminUserId, CancellationToken ct = default)
        {
            ValidateMonthYear(month, year);

            _logger.LogInformation("Triggering bulk monthly draft compensation calculation for {Month}/{Year} by Admin {AdminUserId}",
                month, year, adminUserId);

            return await _compensationRepository.BulkCalculateDraftsAsync(month, year, adminUserId, ct).ConfigureAwait(false);
        }

        public async Task<bool> ApplyAdjustmentAsync(ApplyCompensationAdjustmentViewModel model, int adminUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (model.CompensationId <= 0)
                throw new ArgumentException("Valid Compensation ID is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.AdjustmentType) || !ValidAdjustmentTypes.Contains(model.AdjustmentType.Trim()))
                throw new ArgumentException("Invalid adjustment type. Must be 'Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', or 'Other'.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
                throw new ArgumentException("Adjustment reason must be at least 5 characters.", nameof(model));

            _logger.LogInformation("Applying adjustment {AdjustmentType} ({Amount}) to CompensationId {CompensationId} by Admin {AdminUserId}",
                model.AdjustmentType, model.AdjustmentAmount, model.CompensationId, adminUserId);

            return await _compensationRepository.ApplyAdjustmentAsync(
                model.CompensationId,
                model.AdjustmentType.Trim(),
                model.AdjustmentAmount,
                model.Reason.Trim(),
                adminUserId,
                ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CompensationAdjustmentDto>> GetAdjustmentsAsync(long compensationId, CancellationToken ct = default)
        {
            if (compensationId <= 0)
                throw new ArgumentException("Valid Compensation ID is required.", nameof(compensationId));

            return await _compensationRepository.GetAdjustmentsAsync(compensationId, ct).ConfigureAwait(false);
        }

        public async Task<bool> FinalizeCompensationAsync(long compensationId, int adminUserId, CancellationToken ct = default)
        {
            if (compensationId <= 0)
                throw new ArgumentException("Valid Compensation ID is required.", nameof(compensationId));

            _logger.LogInformation("Finalizing monthly compensation statement ID {CompensationId} by Admin {AdminUserId}",
                compensationId, adminUserId);

            return await _compensationRepository.FinalizeCompensationAsync(compensationId, adminUserId, ct).ConfigureAwait(false);
        }

        public async Task<bool> RecordPayoutAsync(RecordCompensationPayoutViewModel model, int adminUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (model.CompensationId <= 0)
                throw new ArgumentException("Valid Compensation ID is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.PaymentReference))
                throw new ArgumentException("Payment reference is required.", nameof(model));

            var paidAt = model.PaidAt == default ? DateTime.UtcNow : model.PaidAt;

            _logger.LogInformation("Recording payout for CompensationId {CompensationId}, Reference: {PaymentReference} by Admin {AdminUserId}",
                model.CompensationId, model.PaymentReference, adminUserId);

            return await _compensationRepository.RecordPayoutAsync(
                model.CompensationId,
                paidAt,
                model.PaymentReference.Trim(),
                model.Notes?.Trim(),
                adminUserId,
                ct).ConfigureAwait(false);
        }

        private static void ValidateMonthYear(int month, int year)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
            if (year < 2000 || year > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2000 and 2100.");
        }
    }
}
