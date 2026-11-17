using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract orchestrating customer vacation lifecycle,
    /// delivery runsheet lockout checks, approval gate, and early return workflows.
    /// </summary>
    public interface ICustomerVacationService
    {
        Task<IReadOnlyList<CustomerVacationViewModel>> GetCustomerVacationsAsync(int customerId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerVacationViewModel>> GetPendingVacationsAsync(CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> RequestVacationAsync(CustomerVacationViewModel model, int reportedByUserId, bool autoApproveIfAdmin, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ApproveVacationAsync(int vacationId, int approvedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> RejectVacationAsync(int vacationId, string rejectionReason, int rejectedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ResumeEarlyVacationAsync(int vacationId, DateOnly resumedDate, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> CancelVacationAsync(int vacationId, int cancelledByUserId, CancellationToken cancellationToken = default);

        Task<bool> IsDeliveryLockedOutAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default);
    }
}
