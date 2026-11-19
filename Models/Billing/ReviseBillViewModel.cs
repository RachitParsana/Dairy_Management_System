using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// View model for revising a monthly invoice adjustments with optimistic concurrency control.
    /// Multi-payment downward revisions execute deterministic LIFO reversals on allocation events.
    /// </summary>
    public class ReviseBillViewModel
    {
        [Required(ErrorMessage = "Bill ID is required.")]
        public long BillId { get; set; }

        [Required(ErrorMessage = "Expected version is required for optimistic concurrency.")]
        public int ExpectedVersion { get; set; }

        [Range(0.00, 1000000.00, ErrorMessage = "Adjustments cannot be negative and must not exceed ₹10,00,000.")]
        public decimal Adjustments { get; set; }

        [Required(ErrorMessage = "Revision justification reason is required.")]
        [MinLength(5, ErrorMessage = "Revision reason must be at least 5 characters.")]
        [StringLength(500, ErrorMessage = "Revision reason cannot exceed 500 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}
