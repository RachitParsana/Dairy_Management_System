using System;

namespace Dairy_Management_System.Models.Compensation
{
    public class CompensationAdjustmentDto
    {
        public long AdjustmentId { get; set; }
        public long CompensationId { get; set; }
        public string AdjustmentType { get; set; } = string.Empty; // 'Bonus', 'AdvanceDeduction', 'PenaltyDeduction', 'TransitionAdjustment', 'Other'
        public decimal AdjustmentAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
    }
}
