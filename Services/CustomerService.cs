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
    /// Core application service implementing ICustomerService.
    /// Orchestrates customer master workflows, multi-category subscription management,
    /// effective date boundary validation, delivery boy reassignment cases, and deactivation/reactivation.
    /// </summary>
    public class CustomerService : ICustomerService
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly ICustomerSubscriptionRepository _subscriptionRepository;
        private readonly ICustomerVacationRepository _vacationRepository;
        private readonly ILogger<CustomerService> _logger;

        public CustomerService(
            ICustomerRepository customerRepository,
            ICustomerSubscriptionRepository subscriptionRepository,
            ICustomerVacationRepository vacationRepository,
            ILogger<CustomerService> logger)
        {
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
            _vacationRepository = vacationRepository ?? throw new ArgumentNullException(nameof(vacationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CustomerDetailsViewModel?> GetCustomerDetailsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return null;
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var subscriptions = await _subscriptionRepository.GetActiveSubscriptionsByCustomerIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            var customPrices = await _subscriptionRepository.GetCustomPricesByCustomerIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            var assignmentHistory = await _customerRepository.GetAssignmentHistoryAsync(customerId, cancellationToken).ConfigureAwait(false);
            var vacations = await _vacationRepository.GetByCustomerIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            bool isOnVacation = await _vacationRepository.IsCustomerOnApprovedVacationAsync(customerId, today, "Both", cancellationToken).ConfigureAwait(false);

            decimal morningLitres = 0;
            decimal eveningLitres = 0;
            foreach (var sub in subscriptions)
            {
                if (sub.Shift.Equals("Morning", StringComparison.OrdinalIgnoreCase))
                {
                    morningLitres += sub.DailyLiquidVolumeLitres;
                }
                else if (sub.Shift.Equals("Evening", StringComparison.OrdinalIgnoreCase))
                {
                    eveningLitres += sub.DailyLiquidVolumeLitres;
                }
                else if (sub.Shift.Equals("Both", StringComparison.OrdinalIgnoreCase))
                {
                    morningLitres += sub.DailyLiquidVolumeLitres / 2m;
                    eveningLitres += sub.DailyLiquidVolumeLitres / 2m;
                }
            }

            string? currentDeliveryBoyName = null;
            if (assignmentHistory.Count > 0 && assignmentHistory[0].IsActive)
            {
                currentDeliveryBoyName = assignmentHistory[0].DeliveryBoyName;
            }

            return new CustomerDetailsViewModel
            {
                Customer = customer,
                RouteName = string.Empty, // Populated via customer list or view query
                DeliveryBoyName = currentDeliveryBoyName,
                IsOnVacationToday = isOnVacation,
                ActiveSubscriptions = new List<CustomerSubscriptionModel>(subscriptions),
                CustomPrices = new List<CustomerCustomPriceModel>(customPrices),
                AssignmentHistory = new List<CustomerAssignmentHistoryModel>(assignmentHistory),
                RecentVacations = new List<CustomerVacationViewModel>(vacations),
                TotalDailyMorningLitres = morningLitres,
                TotalDailyEveningLitres = eveningLitres,
                TotalDailyVolumeLitres = morningLitres + eveningLitres,
                OutstandingAccountBalance = 0.00m // Computed by billing engine
            };
        }

        public Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomerListAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            return _customerRepository.GetCustomerListAsync(criteria, cancellationToken);
        }

        public Task<int> GetCustomerCountAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            return _customerRepository.GetCustomerCountAsync(criteria, cancellationToken);
        }

        public async Task<CustomerOperationResult> CreateCustomerAsync(CreateCustomerViewModel model, int createdByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            // 1. Phone number uniqueness validation
            bool isPhoneUnique = await _customerRepository.IsPhoneUniqueAsync(model.Phone, null, cancellationToken).ConfigureAwait(false);
            if (!isPhoneUnique)
            {
                return CustomerOperationResult.Failure($"Mobile number '{model.Phone}' is already registered to another active customer.");
            }

            // 2. Generate sequential business CustomerCode
            string customerCode = await _customerRepository.GenerateNextCustomerCodeAsync(cancellationToken).ConfigureAwait(false);

            // 3. Assemble Customer entity
            var customer = new Customer
            {
                CustomerCode = customerCode,
                FullName = model.FullName.Trim(),
                FullNameGujarati = model.FullNameGujarati?.Trim(),
                Phone = model.Phone.Trim(),
                AlternatePhone = model.AlternatePhone?.Trim(),
                Address = model.Address.Trim(),
                Area = model.Area?.Trim(),
                Landmark = model.Landmark?.Trim(),
                RouteId = model.RouteId,
                CurrentDeliveryBoyId = model.CurrentDeliveryBoyId,
                DeliverySequence = model.DeliverySequence,
                PreferredShift = model.PreferredShift,
                Status = "Active",
                Notes = model.Notes?.Trim(),
                DeliveryStartDate = model.DeliveryStartDate != default ? model.DeliveryStartDate : DateOnly.FromDateTime(DateTime.Today),
                IsActive = true,
                CreatedBy = createdByUserId
            };

            int customerId;
            try
            {
                customerId = await _customerRepository.CreateCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create customer '{FullName}'.", model.FullName);
                return CustomerOperationResult.Failure($"Failed to create customer: {ex.Message}");
            }

            // 4. Configure initial subscriptions if provided
            if (model.Subscriptions != null && model.Subscriptions.Count > 0)
            {
                foreach (var line in model.Subscriptions)
                {
                    try
                    {
                        var subModel = new CustomerSubscriptionModel
                        {
                            CustomerId = customerId,
                            CategoryId = line.CategoryId,
                            Shift = line.Shift,
                            PackagingQuantity = line.Quantity,
                            DailyLiquidVolumeLitres = line.Quantity, // Standard 1:1 equivalent for liquid milk
                            DeliverySchedule = line.DeliverySchedule,
                            DeliveryDays = line.DeliveryDays,
                            EffectiveFrom = customer.DeliveryStartDate,
                            EffectiveTo = null,
                            IsActive = true
                        };

                        await _subscriptionRepository.AddSubscriptionAsync(subModel, createdByUserId, cancellationToken).ConfigureAwait(false);

                        // If custom price override was specified on onboarding line
                        if (line.CustomPriceOverride.HasValue && line.CustomPriceOverride.Value > 0)
                        {
                            var priceModel = new CustomerCustomPriceModel
                            {
                                CustomerId = customerId,
                                CategoryId = line.CategoryId,
                                CustomPrice = line.CustomPriceOverride.Value,
                                EffectiveFrom = customer.DeliveryStartDate,
                                EffectiveTo = null,
                                IsActive = true
                            };
                            await _subscriptionRepository.SetCustomPriceAsync(priceModel, createdByUserId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Customer {CustomerId} created, but subscription line failed: {Message}", customerId, ex.Message);
                    }
                }
            }

            return CustomerOperationResult.Success(customerId, customerCode, "Customer registered successfully.");
        }

        public async Task<CustomerOperationResult> UpdateCustomerAsync(EditCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var existing = await _customerRepository.GetByIdAsync(model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            // Phone uniqueness check
            bool isPhoneUnique = await _customerRepository.IsPhoneUniqueAsync(model.Phone, model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (!isPhoneUnique)
            {
                return CustomerOperationResult.Failure($"Mobile number '{model.Phone}' is already in use by another active customer.");
            }

            existing.FullName = model.FullName.Trim();
            existing.FullNameGujarati = model.FullNameGujarati?.Trim();
            existing.Phone = model.Phone.Trim();
            existing.AlternatePhone = model.AlternatePhone?.Trim();
            existing.Address = model.Address.Trim();
            existing.Area = model.Area?.Trim();
            existing.Landmark = model.Landmark?.Trim();
            existing.RouteId = model.RouteId;
            existing.DeliverySequence = model.DeliverySequence;
            existing.PreferredShift = model.PreferredShift;
            existing.Notes = model.Notes?.Trim();
            existing.Status = model.Status;
            existing.UpdatedBy = modifiedByUserId;

            try
            {
                await _customerRepository.UpdateCustomerAsync(existing, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(existing.CustomerId, existing.CustomerCode, "Customer profile updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update customer {CustomerId}.", model.CustomerId);
                return CustomerOperationResult.Failure($"Failed to update customer: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> ReassignDeliveryBoyAsync(CustomerReassignmentViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var customer = await _customerRepository.GetByIdAsync(model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            var today = DateOnly.FromDateTime(DateTime.Today);

            // Case 5: Reject backdated assignment
            if (model.EffectiveDate < today)
            {
                return CustomerOperationResult.Failure("Backdated delivery boy reassignment is strictly prohibited. Historical delivery runsheets and commission statements are immutable.");
            }

            // Case 3: Check if delivery already occurred today
            if (model.EffectiveDate == today)
            {
                bool deliveryRecordedToday = await _customerRepository.HasDeliveryOnDateAsync(model.CustomerId, today, cancellationToken).ConfigureAwait(false);
                if (deliveryRecordedToday)
                {
                    return CustomerOperationResult.Failure(
                        "A delivery has already been recorded for this customer today under the assigned delivery boy. " +
                        $"Reassignment cannot be applied to today. Transfer must take effect tomorrow ({today.AddDays(1):yyyy-MM-dd}).");
                }
            }

            try
            {
                await _customerRepository.ReassignDeliveryBoyAsync(
                    model.CustomerId,
                    model.NewDeliveryBoyId,
                    model.EffectiveDate,
                    model.Reason,
                    modifiedByUserId,
                    cancellationToken).ConfigureAwait(false);

                return CustomerOperationResult.Success(customer.CustomerId, customer.CustomerCode, "Delivery boy assignment updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reassignment failed for customer {CustomerId}.", model.CustomerId);
                return CustomerOperationResult.Failure($"Reassignment failed: {ex.Message}");
            }
        }

        public Task<CustomerOperationResult> DeactivateCustomerAsync(int customerId, string? reason, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            var model = new DeactivateCustomerViewModel
            {
                CustomerId = customerId,
                DeactivationDate = DateOnly.FromDateTime(DateTime.Today),
                Reason = reason,
                ConfirmDeliveryOnDeactivationDate = false
            };
            return DeactivateCustomerAsync(model, modifiedByUserId, cancellationToken);
        }

        public async Task<CustomerOperationResult> DeactivateCustomerAsync(DeactivateCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var customer = await _customerRepository.GetByIdAsync(model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            if (!customer.IsActive || customer.Status.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return CustomerOperationResult.Success(customer.CustomerId, customer.CustomerCode, "Customer is already inactive.");
            }

            // Check if delivery was already recorded on the deactivation date
            bool deliveryExistsOnDate = await _customerRepository.HasDeliveryOnDateAsync(model.CustomerId, model.DeactivationDate, cancellationToken).ConfigureAwait(false);

            if (deliveryExistsOnDate && !model.ConfirmDeliveryOnDeactivationDate)
            {
                string prompt = $"A delivery has already been recorded for this customer on {model.DeactivationDate:yyyy-MM-dd}. " +
                                "Deactivating on this date will bill today's recorded delivery and terminate service starting tomorrow. " +
                                "Please confirm to proceed.";
                return CustomerOperationResult.NeedsConfirmation(model.CustomerId, prompt);
            }

            try
            {
                await _customerRepository.DeactivateCustomerAsync(
                    model.CustomerId,
                    model.DeactivationDate,
                    deliveryExistsOnDate && model.ConfirmDeliveryOnDeactivationDate,
                    modifiedByUserId,
                    cancellationToken).ConfigureAwait(false);

                return CustomerOperationResult.Success(customer.CustomerId, customer.CustomerCode, "Customer deactivated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deactivate customer {CustomerId}.", model.CustomerId);
                return CustomerOperationResult.Failure($"Deactivation failed: {ex.Message}");
            }
        }

        public Task<CustomerOperationResult> ReactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CustomerOperationResult.Failure("Reactivation requires explicit route, sequence, and delivery boy parameters. Please use the reactivation setup workflow."));
        }

        public async Task<CustomerOperationResult> ReactivateCustomerAsync(ReactivateCustomerViewModel model, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var customer = await _customerRepository.GetByIdAsync(model.CustomerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (model.ReactivationDate < today)
            {
                return CustomerOperationResult.Failure("Reactivation date cannot be in the past.");
            }

            try
            {
                // 1. Reactivate Customer master and insert new Delivery Boy assignment
                await _customerRepository.ReactivateCustomerAsync(
                    model.CustomerId,
                    model.ReactivationDate,
                    model.RouteId,
                    model.DeliverySequence,
                    model.DeliveryBoyId,
                    modifiedByUserId,
                    cancellationToken).ConfigureAwait(false);

                // 2. Insert fresh milk subscriptions for the resumed service
                if (model.Subscriptions != null && model.Subscriptions.Count > 0)
                {
                    foreach (var line in model.Subscriptions)
                    {
                        var subModel = new CustomerSubscriptionModel
                        {
                            CustomerId = model.CustomerId,
                            CategoryId = line.CategoryId,
                            Shift = line.Shift,
                            PackagingQuantity = line.Quantity,
                            DailyLiquidVolumeLitres = line.Quantity,
                            DeliverySchedule = line.DeliverySchedule,
                            DeliveryDays = line.DeliveryDays,
                            EffectiveFrom = model.ReactivationDate,
                            EffectiveTo = null,
                            IsActive = true
                        };

                        await _subscriptionRepository.AddSubscriptionAsync(subModel, modifiedByUserId, cancellationToken).ConfigureAwait(false);

                        if (line.CustomPriceOverride.HasValue && line.CustomPriceOverride.Value > 0)
                        {
                            var priceModel = new CustomerCustomPriceModel
                            {
                                CustomerId = model.CustomerId,
                                CategoryId = line.CategoryId,
                                CustomPrice = line.CustomPriceOverride.Value,
                                EffectiveFrom = model.ReactivationDate,
                                EffectiveTo = null,
                                IsActive = true
                            };
                            await _subscriptionRepository.SetCustomPriceAsync(priceModel, modifiedByUserId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                return CustomerOperationResult.Success(customer.CustomerId, customer.CustomerCode, "Customer reactivated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reactivate customer {CustomerId}.", model.CustomerId);
                return CustomerOperationResult.Failure($"Reactivation failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> ConfigureSubscriptionAsync(int customerId, SubscriptionLineInputModel subscriptionInput, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subscriptionInput);

            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            var subModel = new CustomerSubscriptionModel
            {
                CustomerId = customerId,
                CategoryId = subscriptionInput.CategoryId,
                Shift = subscriptionInput.Shift,
                PackagingQuantity = subscriptionInput.Quantity,
                DailyLiquidVolumeLitres = subscriptionInput.Quantity,
                DeliverySchedule = subscriptionInput.DeliverySchedule,
                DeliveryDays = subscriptionInput.DeliveryDays,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.Today),
                EffectiveTo = null,
                IsActive = true
            };

            try
            {
                await _subscriptionRepository.AddSubscriptionAsync(subModel, modifiedByUserId, cancellationToken).ConfigureAwait(false);

                if (subscriptionInput.CustomPriceOverride.HasValue && subscriptionInput.CustomPriceOverride.Value > 0)
                {
                    var priceModel = new CustomerCustomPriceModel
                    {
                        CustomerId = customerId,
                        CategoryId = subscriptionInput.CategoryId,
                        CustomPrice = subscriptionInput.CustomPriceOverride.Value,
                        EffectiveFrom = DateOnly.FromDateTime(DateTime.Today),
                        EffectiveTo = null,
                        IsActive = true
                    };
                    await _subscriptionRepository.SetCustomPriceAsync(priceModel, modifiedByUserId, cancellationToken).ConfigureAwait(false);
                }

                return CustomerOperationResult.Success(customerId, customer.CustomerCode, "Subscription configured successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure subscription for customer {CustomerId}.", customerId);
                return CustomerOperationResult.Failure($"Subscription configuration failed: {ex.Message}");
            }
        }

        public async Task<CustomerOperationResult> SetCustomPriceAsync(int customerId, int categoryId, decimal customPrice, DateOnly effectiveFrom, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
            if (customer == null)
            {
                return CustomerOperationResult.Failure("Customer not found.");
            }

            if (customPrice <= 0)
            {
                return CustomerOperationResult.Failure("Custom price must be positive.");
            }

            var priceModel = new CustomerCustomPriceModel
            {
                CustomerId = customerId,
                CategoryId = categoryId,
                CustomPrice = customPrice,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                IsActive = true
            };

            try
            {
                await _subscriptionRepository.SetCustomPriceAsync(priceModel, modifiedByUserId, cancellationToken).ConfigureAwait(false);
                return CustomerOperationResult.Success(customerId, customer.CustomerCode, "Custom price agreement saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set custom price for customer {CustomerId}.", customerId);
                return CustomerOperationResult.Failure($"Failed to set custom price: {ex.Message}");
            }
        }
    }
}
