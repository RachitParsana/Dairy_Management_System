using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Compensation
{
    public class ApplyCompensationAdjustmentViewModel
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public byte Month { get; set; }
        public short Year { get; set; }
        public string Status { get; set; } = "Draft";
        public decimal GrossEarnings { get; set; }
        public decimal CurrentAdjustments { get; set; }
        public decimal CurrentNetPayable { get; set; }

        [Required(ErrorMessage = "Adjustment type is required.")]
        public string AdjustmentType { get; set; } = "Bonus"; // 'Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other'

        [Required(ErrorMessage = "Adjustment amount is required.")]
        public decimal AdjustmentAmount { get; set; }

        [Required(ErrorMessage = "Reason is required.")]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}
