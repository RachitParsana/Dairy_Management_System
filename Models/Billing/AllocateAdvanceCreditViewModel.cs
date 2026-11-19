using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// View model for allocating existing unallocated advance credit from a payment receipt
    /// to ONE selected invoice.
    /// </summary>
    public class AllocateAdvanceCreditViewModel
    {
        [Required(ErrorMessage = "Payment ID is required.")]
        public long PaymentId { get; set; }

        [Required(ErrorMessage = "Target bill selection is required.")]
        public long TargetBillId { get; set; }

        [Required(ErrorMessage = "Allocated amount is required.")]
        [Range(0.01, 1000000.00, ErrorMessage = "Allocation amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
        public string? Reason { get; set; }
    }
}
