using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract orchestrating high-level Customer management workflows,
    /// business validation, code generation, multi-category subscriptions, and audit logging.
    /// Strictly accessible by Admin accounts.
    /// </summary>
    public interface ICustomerService
    {
        Task<CustomerDetailsViewModel?> GetCustomerDetailsAsync(int customerId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomerListAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default);

        Task<int> GetCustomerCountAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> CreateCustomerAsync(CreateCustomerViewModel model, int createdByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> UpdateCustomerAsync(EditCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ReassignDeliveryBoyAsync(CustomerReassignmentViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> DeactivateCustomerAsync(int customerId, string? reason, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> DeactivateCustomerAsync(DeactivateCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ReactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ReactivateCustomerAsync(ReactivateCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> ConfigureSubscriptionAsync(int customerId, SubscriptionLineInputModel subscriptionInput, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<CustomerOperationResult> SetCustomPriceAsync(int customerId, int categoryId, decimal customPrice, DateOnly effectiveFrom, int modifiedByUserId, CancellationToken cancellationToken = default);
    }
}
