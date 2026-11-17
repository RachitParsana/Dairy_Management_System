using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Input model for delivery boy reassignment or unassignment modal.
    /// Supports transactionally transferring a customer to a new delivery boy with effective dates.
    /// </summary>
    public class CustomerReassignmentViewModel
    {
        [Required]
        public int CustomerId { get; set; }

        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? CurrentDeliveryBoyId { get; set; }
        public string? CurrentDeliveryBoyName { get; set; }

        [Display(Name = "New Delivery Boy")]
        public int? NewDeliveryBoyId { get; set; } // Null represents unassignment

        [Required(ErrorMessage = "Effective transfer date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Effective Date")]
        public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        [Display(Name = "Reassignment Reason / Notes")]
        public string? Reason { get; set; }
    }
}
