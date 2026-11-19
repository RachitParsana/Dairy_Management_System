using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Compensation
{
    public class RecordCompensationPayoutViewModel
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public byte Month { get; set; }
        public short Year { get; set; }
        public decimal NetPayableAmount { get; set; }

        [Required(ErrorMessage = "Payment date and time is required.")]
        public DateTime PaidAt { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "Payment reference is required.")]
        [StringLength(50, ErrorMessage = "Payment reference cannot exceed 50 characters.")]
        public string PaymentReference { get; set; } = string.Empty;

        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        public string? Notes { get; set; }
    }
}
