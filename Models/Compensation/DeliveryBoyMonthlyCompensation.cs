using System;

namespace Dairy_Management_System.Models.Compensation
{
    public class DeliveryBoyMonthlyCompensation
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public byte Month { get; set; }
        public short Year { get; set; }
        public string CompensationType { get; set; } = "Commission"; // 'Fixed Salary' or 'Commission'
        public decimal EligibleCompletedLitres { get; set; }
        public decimal ApplicableCommissionRate { get; set; }
        public decimal CalculatedCommission { get; set; }
        public decimal FixedSalaryAmount { get; set; }
        public decimal GrossEarnings => CompensationType == "Fixed Salary" ? FixedSalaryAmount : CalculatedCommission;
        public decimal Adjustments { get; set; }
        public decimal NetPayableAmount { get; set; }
        public string Status { get; set; } = "Draft"; // 'Draft', 'Finalized', 'Paid'
        public DateTime? FinalizedAt { get; set; }
        public int? FinalizedBy { get; set; }
        public DateTime? PaidAt { get; set; }
        public int? PaidBy { get; set; }
        public string? PaymentReference { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
