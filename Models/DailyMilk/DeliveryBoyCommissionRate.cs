using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents an effective-dated per-litre commission rate tier for a Delivery Boy.
    /// Maps to database table dbo.DeliveryBoyCommissionRates.
    /// Concurrency-safe overlap prevention guarantees historical rates are preserved indefinitely.
    /// Zero silent fallback: if no verified rate tier covers a delivery date, shift finalization is rejected.
    /// </summary>
    public class DeliveryBoyCommissionRate
    {
        public int RateId { get; set; }

        [Required]
        public int DeliveryBoyId { get; set; }

        public string? DeliveryBoyName { get; set; }

        /// <summary>
        /// Commission amount per eligible liquid milk litre (₹/L).
        /// </summary>
        [Required]
        [Range(0.00, 1000.00, ErrorMessage = "Commission rate must be non-negative.")]
        public decimal CommissionRate { get; set; }

        /// <summary>
        /// Inclusive start date from which this commission rate applies.
        /// Strictly required and verified by an Administrator. Never automatically guessed.
        /// </summary>
        [Required]
        public DateOnly EffectiveFrom { get; set; }

        /// <summary>
        /// Inclusive end date through which this commission rate applies.
        /// Null indicates the currently open-ended active rate tier.
        /// </summary>
        public DateOnly? EffectiveTo { get; set; }

        /// <summary>
        /// Optional administrative notes explaining why the rate tier was introduced or adjusted.
        /// </summary>
        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int CreatedBy { get; set; }

        public string? CreatedByName { get; set; }

        /// <summary>
        /// Evaluates whether this rate tier covers a given calendar date.
        /// </summary>
        public bool IsEffectiveOn(DateOnly date) =>
            EffectiveFrom <= date && (EffectiveTo == null || EffectiveTo.Value >= date);

        /// <summary>
        /// Evaluates whether this tier is currently active as of today.
        /// </summary>
        public bool IsCurrentActive => IsEffectiveOn(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>
    /// Request model for an Admin configuring a new effective-dated commission rate tier for a delivery boy.
    /// </summary>
    public class CreateDeliveryBoyCommissionRateViewModel
    {
        [Required]
        public int DeliveryBoyId { get; set; }

        [Required(ErrorMessage = "Commission rate is required.")]
        [Range(0.00, 1000.00, ErrorMessage = "Commission rate must be non-negative.")]
        public decimal CommissionRate { get; set; }

        [Required(ErrorMessage = "Effective start date is required.")]
        public DateOnly EffectiveFrom { get; set; }

        public DateOnly? EffectiveTo { get; set; }

        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Request model for an Admin initializing the baseline commission rate for an existing delivery boy
    /// who does not yet have verified historical rate records. Requires an explicit verified EffectiveFrom date.
    /// </summary>
    public class InitializeDeliveryBoyCommissionRateViewModel
    {
        [Required]
        public int DeliveryBoyId { get; set; }

        [Required(ErrorMessage = "Verified commission rate is required.")]
        [Range(0.00, 1000.00, ErrorMessage = "Commission rate must be non-negative.")]
        public decimal CommissionRate { get; set; }

        /// <summary>
        /// Earliest calendar date for which this baseline rate is authorized.
        /// Deliveries logged prior to this date cannot be finalized until an earlier rate tier is established.
        /// </summary>
        [Required(ErrorMessage = "Verified EffectiveFrom date is mandatory. Cannot be defaulted to today.")]
        public DateOnly EffectiveFrom { get; set; }

        [Required(ErrorMessage = "Initialization notes/justification are required.")]
        [MinLength(5, ErrorMessage = "Justification must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Justification cannot exceed 250 characters.")]
        public string Justification { get; set; } = string.Empty;
    }
}
