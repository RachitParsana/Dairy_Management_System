using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Repository contract for customer multi-category subscription lines
    /// and custom pricing agreements against dbo.CustomerSubscriptions and dbo.CustomerCustomPricing.
    /// </summary>
    public interface ICustomerSubscriptionRepository
    {
        Task<IReadOnlyList<CustomerSubscriptionModel>> GetActiveSubscriptionsByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerSubscriptionModel>> GetSubscriptionsForDateAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default);

        Task AddSubscriptionAsync(CustomerSubscriptionModel subscription, int createdByUserId, CancellationToken cancellationToken = default);

        Task EndSubscriptionAsync(int subscriptionId, DateOnly effectiveTo, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CustomerCustomPriceModel>> GetCustomPricesByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);

        Task<decimal?> GetEffectivePriceAsync(int customerId, int categoryId, DateOnly targetDate, CancellationToken cancellationToken = default);

        Task SetCustomPriceAsync(CustomerCustomPriceModel priceModel, int modifiedByUserId, CancellationToken cancellationToken = default);

        Task EndCustomPriceAsync(int pricingId, DateOnly effectiveTo, int modifiedByUserId, CancellationToken cancellationToken = default);
    }
}
