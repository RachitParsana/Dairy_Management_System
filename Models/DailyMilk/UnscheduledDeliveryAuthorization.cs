using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents an explicit, audited Administrator authorization permitting an unscheduled delivery
    /// for a customer on an off-schedule calendar date.
    /// Maps to database table dbo.CustomerUnscheduledDeliveryAuthorizations.
    /// Supports 'Morning', 'Evening', or 'Both' authorized shifts.
    /// Consumption is tracked independently per shift in dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions.
    /// </summary>
    public class UnscheduledDeliveryAuthorization
    {
        public long AuthorizationId { get; set; }

        [Required]
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }

        /// <summary>
        /// Optional dairy category authorized (null = all subscribed categories).
        /// </summary>
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }

        [Required]
        public DateOnly EntryDate { get; set; }

        /// <summary>
        /// Authorized shift scope: 'Morning', 'Evening', or 'Both'.
        /// </summary>
        [Required]
        [RegularExpression("^(Morning|Evening|Both)$", ErrorMessage = "Authorized shift must be 'Morning', 'Evening', or 'Both'.")]
        public string AuthorizedShift { get; set; } = "Morning";

        /// <summary>
        /// Backward-compatible alias for AuthorizedShift.
        /// </summary>
        public string Shift
        {
            get => AuthorizedShift;
            set => AuthorizedShift = value;
        }

        /// <summary>
        /// Optional maximum allowed packaging quantity for this authorized delivery.
        /// </summary>
        [Range(0.001, 1000.000, ErrorMessage = "Maximum quantity must be greater than zero.")]
        public decimal? MaxQuantity { get; set; }

        /// <summary>
        /// Mandatory business justification explaining why the exception was granted.
        /// </summary>
        [Required(ErrorMessage = "Authorization reason is mandatory.")]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key referencing the Admin in dbo.Users who authorized the exception.
        /// </summary>
        public int AuthorizedBy { get; set; }
        public string? AuthorizedByName { get; set; }

        /// <summary>
        /// Timestamp when authorization was granted (UTC).
        /// </summary>
        public DateTime AuthorizedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Flag indicating whether the Admin revoked the authorization prior to consumption.
        /// </summary>
        public bool IsRevoked { get; set; }

        /// <summary>
        /// Admin who revoked the authorization.
        /// </summary>
        public int? RevokedBy { get; set; }

        /// <summary>
        /// Timestamp when revoked (UTC).
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>
        /// Business justification for revocation.
        /// </summary>
        public string? RevokeReason { get; set; }

        /// <summary>
        /// Shift-specific consumption records for this authorization.
        /// </summary>
        public List<UnscheduledDeliveryAuthorizationConsumption> Consumptions { get; set; } = new();

        /// <summary>
        /// Checks whether the Morning shift has already been consumed.
        /// </summary>
        public bool IsMorningConsumed => Consumptions.Any(c => c.ConsumedShift == "Morning");

        /// <summary>
        /// Checks whether the Evening shift has already been consumed.
        /// </summary>
        public bool IsEveningConsumed => Consumptions.Any(c => c.ConsumedShift == "Evening");

        /// <summary>
        /// Evaluates whether the authorization has been fully consumed for all authorized shifts.
        /// </summary>
        public bool IsFullyConsumed
        {
            get
            {
                if (AuthorizedShift == "Morning") return IsMorningConsumed;
                if (AuthorizedShift == "Evening") return IsEveningConsumed;
                if (AuthorizedShift == "Both") return IsMorningConsumed && IsEveningConsumed;
                return false;
            }
        }

        /// <summary>
        /// Checks whether a specific delivery shift is still available for consumption under this authorization.
        /// </summary>
        public bool IsShiftAvailable(string shift)
        {
            if (IsRevoked) return false;
            if (AuthorizedShift != "Both" && AuthorizedShift != shift) return false;
            return !Consumptions.Any(c => c.ConsumedShift == shift);
        }

        /// <summary>
        /// Backward-compatible property for whether the authorization has any consumption.
        /// </summary>
        public bool IsUsed => Consumptions.Count > 0;

        /// <summary>
        /// Evaluates whether this authorization is active and has at least one unconsumed shift available.
        /// </summary>
        public bool IsActiveAndAvailable => !IsRevoked && !IsFullyConsumed;
    }

    /// <summary>
    /// Represents a normalized shift-specific consumption event of an unscheduled delivery authorization.
    /// Maps to database table dbo.CustomerUnscheduledDeliveryAuthorizationConsumptions.
    /// Enforces at most one consumption per authorization per shift.
    /// </summary>
    public class UnscheduledDeliveryAuthorizationConsumption
    {
        public long ConsumptionId { get; set; }

        [Required]
        public long AuthorizationId { get; set; }

        [Required]
        public long DeliveryId { get; set; }

        [Required]
        [RegularExpression("^(Morning|Evening)$", ErrorMessage = "Consumed shift must be 'Morning' or 'Evening'.")]
        public string ConsumedShift { get; set; } = "Morning";

        public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Request model for an Admin granting an unscheduled delivery exception.
    /// </summary>
    public class CreateUnscheduledAuthorizationViewModel
    {
        [Required]
        public int CustomerId { get; set; }

        public int? CategoryId { get; set; }

        [Required]
        public DateOnly EntryDate { get; set; }

        [Required]
        [RegularExpression("^(Morning|Evening|Both)$", ErrorMessage = "Shift must be 'Morning', 'Evening', or 'Both'.")]
        public string Shift { get; set; } = "Morning";

        [Range(0.001, 1000.000, ErrorMessage = "Max quantity must be greater than zero.")]
        public decimal? MaxQuantity { get; set; }

        [Required(ErrorMessage = "Authorization justification is mandatory.")]
        [MinLength(5, ErrorMessage = "Justification must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Justification cannot exceed 250 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}
