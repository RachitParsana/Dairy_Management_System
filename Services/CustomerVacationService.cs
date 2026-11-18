using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Repositories;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Application service implementing ICustomerVacationService.
    /// Manages customer absence lifecycle, 6-pair conflict matrix overlap validation,
    /// administrative approval workflow, runsheet lockout evaluation, and early returns.
    /// </summary>
    public class CustomerVacationService : ICustomerVacationService
    {
        private readonly ICustomerVacationRepository _vacationRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<CustomerVacationService> _logger;

        public CustomerVacationService(
            ICustomerVacationRepository vacationRepository,
            ICustomerRepository customerRepository,
            ILogger<CustomerVacationService> logger)
        {
            _vacationRepository = vacationRepository ?? throw new ArgumentNullException(nameof(vacationRepository));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<CustomerVacationViewModel>> GetCustomerVacationsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            return _vacationRepository.GetByCustomerIdAsync(customerId, cancellationToken);
        }

        public Task<IReadOnlyList<CustomerVacationViewModel>> GetPendingVacationsAsync(CancellationToken cancellationToken = default)
        {
            return _vacationRepository.GetPendingVacationsAsync(cancellationToken);
        }

        public async Task<CustomerOperationResult> RequestVacationAsync(CustomerVacationViewModel model, int reportedByUserId, bool autoApproveIfAdmin, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            // 1. Verify customer exists and is active
            var customer = await _customerRepository.GetByIdAsync(model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (customer == null || !customer.IsActive)
            {
                return CustomerOperationResult.Failure("Target customer does not exist or is inactive.");
            }

            // 2. Validate date interval
            if (model.EndDate < model.StartDate)
            {
                return CustomerOperationResult.Failure("Vacation end date cannot be earlier than start date.");
            }

            // Maximum allowed window: 90 days
            if (model.EndDate.DayNumber - model.StartDate.DayNumber > 90)
            {
                return CustomerOperationResult.Failure("Vacation duration cannot exceed 90 days in a single request.");
            }

            // 3. Shift validation
            if (!model.Shift.Equals("Morning", StringComparison.OrdinalIgnoreCase) &&
                !model.Shift.Equals("Evening", StringComparison.OrdinalIgnoreCase) &&
                !model.Shift.Equals("Both", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Failure("Invalid shift specified. Must be 'Morning', 'Evening', or 'Both'.");
            }

            // 4. Overlap conflict matrix pre-check (6-pair conflict evaluation)
            bool hasOverlap = await _vacationRepository.HasOverlappingVacationAsync(
                model.CustomerId,
                model.StartDate,
                model.EndDate,
                model.Shift,
                null,
                cancellationToken).ConfigureAwait(false);

            if (hasOverlap)
            {
                return CustomerOperationResult.Failure(
                    $"An active or pending vacation already covers this customer for shift '{model.Shift}' " +
                    $"between {model.StartDate:yyyy-MM-dd} and {model.EndDate:yyyy-MM-dd}.");
            }

            // 5. Configure status and approval metadata
            if (autoApproveIfAdmin)
            {
                model.Status = "Approved";
                model.ApprovedBy = reportedByUserId;
                model.ApprovedAt = DateTime.UtcNow;
            }
            else
            {
                model.Status = "Pending";
                model.ApprovedBy = null;
                model.ApprovedAt = null;
            }

            try
            {
                int vacationId = await _vacationRepository.CreateVacationRequestAsync(model, reportedByUserId, cancellationToken).ConfigureAwait(false);
                string message = autoApproveIfAdmin
                    ? "Vacation request registered and approved successfully."
                    : "Vacation request submitted for administrative approval.";
                return CustomerOperationResult.Success(model.CustomerId, customer.CustomerCode, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create vacation request for customer {CustomerId}.", model.CustomerId);
                return CustomerOperationResult.Failure($"Vacation registration failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> ApproveVacationAsync(int vacationId, int approvedByUserId, CancellationToken cancellationToken = default)
        {
            var vacation = await _vacationRepository.GetByIdAsync(vacationId, cancellationToken).ConfigureAwait(false);
            if (vacation == null)
            {
                return CustomerOperationResult.Failure("Vacation request not found.");
            }

            if (!vacation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Failure($"Cannot approve vacation in '{vacation.Status}' status.");
            }

            // Concurrency safety: re-check for any conflicting approved vacations that might have been approved meanwhile
            bool hasConflict = await _vacationRepository.HasOverlappingVacationAsync(
                vacation.CustomerId,
                vacation.StartDate,
                vacation.EndDate,
                vacation.Shift,
                vacationId,
                cancellationToken).ConfigureAwait(false);

            if (hasConflict)
            {
                return CustomerOperationResult.Failure("Cannot approve vacation: A conflicting approved vacation already covers this window.");
            }

            try
            {
                await _vacationRepository.ApproveVacationAsync(vacationId, approvedByUserId, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(vacation.CustomerId, vacation.CustomerCode, "Vacation request approved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to approve vacation {VacationId}.", vacationId);
                return CustomerOperationResult.Failure($"Approval failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> RejectVacationAsync(int vacationId, string rejectionReason, int rejectedByUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                return CustomerOperationResult.Failure("Rejection reason is required.");
            }

            var vacation = await _vacationRepository.GetByIdAsync(vacationId, cancellationToken).ConfigureAwait(false);
            if (vacation == null)
            {
                return CustomerOperationResult.Failure("Vacation request not found.");
            }

            if (!vacation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Failure($"Cannot reject vacation in '{vacation.Status}' status.");
            }

            try
            {
                await _vacationRepository.RejectVacationAsync(vacationId, rejectionReason.Trim(), rejectedByUserId, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(vacation.CustomerId, vacation.CustomerCode, "Vacation request rejected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reject vacation {VacationId}.", vacationId);
                return CustomerOperationResult.Failure($"Rejection failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> ResumeEarlyVacationAsync(int vacationId, DateOnly resumedDate, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            var vacation = await _vacationRepository.GetByIdAsync(vacationId, cancellationToken).ConfigureAwait(false);
            if (vacation == null)
            {
                return CustomerOperationResult.Failure("Vacation record not found.");
            }

            if (!vacation.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Failure("Early resumption is only applicable to Approved vacations.");
            }

            if (resumedDate < vacation.StartDate || resumedDate > vacation.EndDate)
            {
                return CustomerOperationResult.Failure(
                    $"Early resumption date ({resumedDate:yyyy-MM-dd}) must fall between vacation start date " +
                    $"({vacation.StartDate:yyyy-MM-dd}) and end date ({vacation.EndDate:yyyy-MM-dd}).");
            }

            try
            {
                await _vacationRepository.ResumeEarlyVacationAsync(vacationId, resumedDate, modifiedByUserId, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(
                    vacation.CustomerId,
                    vacation.CustomerCode,
                    $"Early resumption confirmed. Deliveries will resume on {resumedDate:yyyy-MM-dd}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record early resumption for vacation {VacationId}.", vacationId);
                return CustomerOperationResult.Failure($"Early resumption failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> CancelVacationAsync(int vacationId, int cancelledByUserId, CancellationToken cancellationToken = default)
        {
            var vacation = await _vacationRepository.GetByIdAsync(vacationId, cancellationToken).ConfigureAwait(false);
            if (vacation == null)
            {
                return CustomerOperationResult.Failure("Vacation record not found.");
            }

            if (!vacation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
                !vacation.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Failure($"Cannot cancel vacation in '{vacation.Status}' status.");
            }

            try
            {
                await _vacationRepository.CancelVacationAsync(vacationId, cancelledByUserId, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(vacation.CustomerId, vacation.CustomerCode, "Vacation request cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel vacation {VacationId}.", vacationId);
                return CustomerOperationResult.Failure($"Cancellation failed: {ex.Message}");
            }
        }

        public Task<bool> IsDeliveryLockedOutAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default)
        {
            return _vacationRepository.IsCustomerOnApprovedVacationAsync(customerId, targetDate, shift, cancellationToken);
        }
    }
}
