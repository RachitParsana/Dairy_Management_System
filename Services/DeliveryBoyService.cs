using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DeliveryBoy;
using Dairy_Management_System.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Production-grade application service orchestrating business rules, password security,
    /// and lifecycle operations for Delivery Boy Management.
    /// </summary>
    public class DeliveryBoyService : IDeliveryBoyService
    {
        private readonly IDeliveryBoyRepository _deliveryBoyRepository;
        private readonly IPasswordHasher<UserAuthRecord> _passwordHasher;
        private readonly ILogger<DeliveryBoyService> _logger;

        public DeliveryBoyService(
            IDeliveryBoyRepository deliveryBoyRepository,
            IPasswordHasher<UserAuthRecord> passwordHasher,
            ILogger<DeliveryBoyService> logger)
        {
            _deliveryBoyRepository = deliveryBoyRepository ?? throw new ArgumentNullException(nameof(deliveryBoyRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> CreateDeliveryBoyAsync(CreateDeliveryBoyViewModel model, int adminUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (string.IsNullOrWhiteSpace(model.FullName))
                throw new ArgumentException("Full Name is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.Username))
                throw new ArgumentException("Username is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.Phone))
                throw new ArgumentException("Phone number is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 6)
                throw new ArgumentException("Password must be at least 6 characters.", nameof(model));
            if (model.PrimaryRouteId <= 0)
                throw new ArgumentException("Valid Primary Route is required.", nameof(model));

            if (model.CompensationType != "Fixed Salary" && model.CompensationType != "Commission")
                throw new ArgumentException("Compensation type must be 'Fixed Salary' or 'Commission'.", nameof(model));

            if (model.CompensationType == "Fixed Salary")
            {
                if (model.MonthlyBaseSalary < 0)
                    throw new ArgumentException("Monthly Base Salary cannot be negative.", nameof(model));
                model.InitialCommissionRate = 0.00m;
            }
            else // Commission
            {
                if (model.InitialCommissionRate <= 0)
                    throw new ArgumentException("Initial Commission Rate must be greater than zero.", nameof(model));
                if (!model.EffectiveFrom.HasValue)
                    throw new ArgumentException("Effective From date is required for commission-based delivery boys.", nameof(model));
                model.MonthlyBaseSalary = 0.00m;
            }

            _logger.LogInformation("Registering delivery boy {Username} under model {CompensationType} by Admin {AdminUserId}",
                model.Username.Trim(), model.CompensationType, adminUserId);

            // Hash password cryptographically (plaintext password is never passed to SQL or logged)
            var dummyUser = new UserAuthRecord { Username = model.Username.Trim() };
            var passwordHash = _passwordHasher.HashPassword(dummyUser, model.Password);

            var dto = new CreateDeliveryBoyDto
            {
                FullName = model.FullName.Trim(),
                Username = model.Username.Trim(),
                Phone = model.Phone.Trim(),
                PasswordHash = passwordHash,
                VehicleNumber = model.VehicleNumber?.Trim(),
                PrimaryRouteId = model.PrimaryRouteId,
                CompensationType = model.CompensationType,
                MonthlyBaseSalary = model.MonthlyBaseSalary,
                InitialCommissionRate = model.InitialCommissionRate,
                EffectiveFrom = model.EffectiveFrom,
                Notes = model.Notes?.Trim(),
                AdminUserId = adminUserId
            };

            var id = await _deliveryBoyRepository.CreateDeliveryBoyAsync(dto, ct).ConfigureAwait(false);
            _logger.LogInformation("Successfully registered delivery boy with ID {DeliveryBoyId}", id);
            return id;
        }

        public async Task<DeliveryBoyDetailsDto?> GetDetailsAsync(int deliveryBoyId, CancellationToken ct = default)
        {
            if (deliveryBoyId <= 0)
                throw new ArgumentException("Valid Delivery Boy ID is required.", nameof(deliveryBoyId));

            return await _deliveryBoyRepository.GetByIdAsync(deliveryBoyId, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DeliveryBoyListItemDto>> GetDirectoryAsync(DeliveryBoyFilterCriteria criteria, CancellationToken ct = default)
        {
            return await _deliveryBoyRepository.GetDirectoryAsync(criteria ?? new DeliveryBoyFilterCriteria(), ct).ConfigureAwait(false);
        }

        public async Task<EditDeliveryBoyViewModel?> GetForEditAsync(int deliveryBoyId, CancellationToken ct = default)
        {
            var details = await GetDetailsAsync(deliveryBoyId, ct).ConfigureAwait(false);
            if (details == null) return null;

            return new EditDeliveryBoyViewModel
            {
                DeliveryBoyId = details.DeliveryBoyId,
                FullName = details.FullName,
                Phone = details.Phone,
                VehicleNumber = details.VehicleNumber,
                PrimaryRouteId = details.PrimaryRouteId ?? 0,
                CompensationType = details.CompensationType,
                MonthlyBaseSalary = details.MonthlyBaseSalary,
                CurrentCommissionRate = details.CurrentCommissionRate
            };
        }

        public async Task<bool> UpdateProfileAsync(EditDeliveryBoyViewModel model, int adminUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (model.DeliveryBoyId <= 0)
                throw new ArgumentException("Valid Delivery Boy ID is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.FullName))
                throw new ArgumentException("Full Name is required.", nameof(model));
            if (string.IsNullOrWhiteSpace(model.Phone))
                throw new ArgumentException("Phone number is required.", nameof(model));
            if (model.PrimaryRouteId <= 0)
                throw new ArgumentException("Valid Primary Route is required.", nameof(model));

            _logger.LogInformation("Updating delivery boy profile ID {DeliveryBoyId} by Admin {AdminUserId}",
                model.DeliveryBoyId, adminUserId);

            var dto = new EditDeliveryBoyDto
            {
                DeliveryBoyId = model.DeliveryBoyId,
                FullName = model.FullName.Trim(),
                Phone = model.Phone.Trim(),
                VehicleNumber = model.VehicleNumber?.Trim(),
                PrimaryRouteId = model.PrimaryRouteId,
                MonthlyBaseSalary = model.CompensationType == "Fixed Salary" ? model.MonthlyBaseSalary : null,
                AdminUserId = adminUserId
            };

            return await _deliveryBoyRepository.UpdateProfileAsync(dto, ct).ConfigureAwait(false);
        }

        public async Task<bool> DeactivateAsync(int deliveryBoyId, int? reassignToDeliveryBoyId, string reason, int adminUserId, CancellationToken ct = default)
        {
            if (deliveryBoyId <= 0)
                throw new ArgumentException("Valid Delivery Boy ID is required.", nameof(deliveryBoyId));
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                throw new ArgumentException("Deactivation reason must be at least 5 characters.", nameof(reason));
            if (reassignToDeliveryBoyId.HasValue && reassignToDeliveryBoyId.Value == deliveryBoyId)
                throw new ArgumentException("Cannot reassign customers to the same delivery boy being deactivated.", nameof(reassignToDeliveryBoyId));

            _logger.LogWarning("Deactivating delivery boy ID {DeliveryBoyId} with ReassignTo: {ReassignTo} by Admin {AdminUserId}",
                deliveryBoyId, reassignToDeliveryBoyId, adminUserId);

            return await _deliveryBoyRepository.DeactivateAsync(deliveryBoyId, reassignToDeliveryBoyId, reason.Trim(), adminUserId, ct).ConfigureAwait(false);
        }

        public async Task<bool> ReactivateAsync(int deliveryBoyId, decimal? newCommissionRate, int adminUserId, CancellationToken ct = default)
        {
            if (deliveryBoyId <= 0)
                throw new ArgumentException("Valid Delivery Boy ID is required.", nameof(deliveryBoyId));

            _logger.LogInformation("Reactivating delivery boy ID {DeliveryBoyId} by Admin {AdminUserId}", deliveryBoyId, adminUserId);

            return await _deliveryBoyRepository.ReactivateAsync(deliveryBoyId, newCommissionRate, adminUserId, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DeliveryBoyLookupItem>> GetActiveLookupAsync(CancellationToken ct = default)
        {
            return await _deliveryBoyRepository.GetActiveLookupAsync(ct).ConfigureAwait(false);
        }
    }
}
