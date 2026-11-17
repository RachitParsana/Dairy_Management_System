using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Repository contract for customer vacation requests, approvals, and early return operations
    /// against dbo.CustomerVacations.
    /// </summary>
    public interface ICustomerVacationRepository
    {
        Task<CustomerVacationViewModel?> GetByIdAsync(int vacationId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerVacationViewModel>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerVacationViewModel>> GetPendingVacationsAsync(CancellationToken cancellationToken = default);

        Task<bool> IsCustomerOnApprovedVacationAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default);

        Task<bool> HasOverlappingVacationAsync(int customerId, DateOnly startDate, DateOnly endDate, string shift, int? excludeVacationId = null, CancellationToken cancellationToken = default);

        Task<int> CreateVacationRequestAsync(CustomerVacationViewModel vacation, int reportedByUserId, CancellationToken cancellationToken = default);

        Task ApproveVacationAsync(int vacationId, int approvedByUserId, CancellationToken cancellationToken = default);

        Task RejectVacationAsync(int vacationId, string rejectionReason, int rejectedByUserId, CancellationToken cancellationToken = default);

        Task ResumeEarlyVacationAsync(int vacationId, DateOnly resumedDate, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task CancelVacationAsync(int vacationId, int cancelledByUserId, CancellationToken cancellationToken = default);
    }
}
