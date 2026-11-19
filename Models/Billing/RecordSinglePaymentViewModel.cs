using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// View model for recording a manual payment receipt and allocating it strictly
    /// to ONE selected invoice. Any excess payment is retained as unallocated advance credit.
    /// </summary>
    public class RecordSinglePaymentViewModel
    {
        [Required(ErrorMessage = "Customer selection is required.")]
        public int CustomerId { get; set; }

        [Required(ErrorMessage = "Payment amount is required.")]
        [Range(0.01, 1000000.00, ErrorMessage = "Payment amount must be greater than zero and at most ₹10,00,000.")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Payment date is required.")]
        public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required(ErrorMessage = "Payment mode is required.")]
        [RegularExpression("^(Online|Offline)$", ErrorMessage = "Mode must be Online or Offline.")]
        public string PaymentMode { get; set; } = "Offline";

        [StringLength(100, ErrorMessage = "Reference number cannot exceed 100 characters.")]
        public string? ReferenceNumber { get; set; }

        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
        public string? Notes { get; set; }

        // Strictly ONE selected bill for allocation
        [Required(ErrorMessage = "Target bill selection is required.")]
        public long TargetBillId { get; set; }
    }
}
