using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Repository contract for Customer master persistence operations against dbo.Customers
    /// and dbo.CustomerDeliveryBoyAssignments.
    /// Pure ADO.NET using Microsoft.Data.SqlClient.
    /// </summary>
    public interface ICustomerRepository
    {
        /// <summary>
        /// Retrieves a customer domain entity by surrogate CustomerId.
        /// </summary>
        Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a customer domain entity by unique business CustomerCode.
        /// </summary>
        Task<Customer?> GetByCodeAsync(string customerCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a paginated and filtered list of customers for grid rendering.
        /// </summary>
        Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomerListAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves the total count matching filter criteria for pagination calculation.
        /// </summary>
        Task<int> GetCustomerCountAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all customers currently assigned to a specific delivery boy.
        /// Enforces route boundary isolation for delivery boy mobile views.
        /// </summary>
        Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomersByDeliveryBoyAsync(int deliveryBoyId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts a new customer record and returns the generated CustomerId.
        /// </summary>
        Task<int> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates basic demographic and delivery preference details for an existing customer.
        /// </summary>
        Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deactivates a customer (IsActive = 0, Status = 'Inactive') and closes active assignments.
        /// </summary>
        Task DeactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deactivates a customer with explicit effective date boundary semantics and delivery handling.
        /// </summary>
        Task DeactivateCustomerAsync(int customerId, DateOnly deactivationDate, bool deliveryAlreadyRecordedOnDate, int modifiedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether any delivered/recorded milk delivery exists for a customer on a specific date.
        /// </summary>
        Task<bool> HasDeliveryOnDateAsync(int customerId, DateOnly date, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reactivates a previously inactive customer (IsActive = 1, Status = 'Active').
        /// </summary>
        Task ReactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reactivates a customer starting on a designated resumption date with new route, sequence, and delivery boy.
        /// </summary>
        Task ReactivateCustomerAsync(int customerId, DateOnly reactivationDate, int routeId, int deliverySequence, int deliveryBoyId, int modifiedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reassigns or unassigns a customer's delivery boy with full assignment history tracking.
        /// </summary>
        Task ReassignDeliveryBoyAsync(int customerId, int? newDeliveryBoyId, DateOnly effectiveDate, string? notes, int modifiedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves delivery boy assignment history for a customer.
        /// </summary>
        Task<IReadOnlyList<CustomerAssignmentHistoryModel>> GetAssignmentHistoryAsync(int customerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates the next sequential unique CustomerCode in format 'CUST-XXXXX'.
        /// </summary>
        Task<string> GenerateNextCustomerCodeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a mobile phone number is already registered to another active customer.
        /// </summary>
        Task<bool> IsPhoneUniqueAsync(string phone, int? excludeCustomerId = null, CancellationToken cancellationToken = default);
    }
}
