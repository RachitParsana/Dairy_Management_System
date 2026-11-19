using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// View model for voiding a customer payment receipt.
    /// Strictly requires a justification of at least 10 characters.
    /// </summary>
    public class VoidPaymentViewModel
    {
        [Required(ErrorMessage = "Payment ID is required.")]
        public long PaymentId { get; set; }

        [Required(ErrorMessage = "Void justification reason is mandatory.")]
        [MinLength(10, ErrorMessage = "Void justification reason must be at least 10 characters.")]
        [StringLength(500, ErrorMessage = "Void reason cannot exceed 500 characters.")]
        public string VoidReason { get; set; } = string.Empty;
    }
}
