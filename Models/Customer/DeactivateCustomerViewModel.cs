using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Input ViewModel for Customer Deactivation workflow.
    /// Captures the target deactivation date, reason, and explicit confirmation
    /// if deliveries have already been recorded on the target deactivation date.
    /// </summary>
    public class DeactivateCustomerViewModel
    {
        [Required]
        public int CustomerId { get; set; }

        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Deactivation effective date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Deactivation Date")]
        public DateOnly DeactivationDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
        [Display(Name = "Reason for Deactivation")]
        public string? Reason { get; set; }

        /// <summary>
        /// Explicit admin acknowledgement if a delivery was already recorded on the deactivation date.
        /// When true, confirms that recorded delivery on DeactivationDate is billed and service ends next day.
        /// </summary>
        public bool ConfirmDeliveryOnDeactivationDate { get; set; }
    }
}
