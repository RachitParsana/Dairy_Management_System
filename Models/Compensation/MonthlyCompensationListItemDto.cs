using System;

namespace Dairy_Management_System.Models.Compensation
{
    public class MonthlyCompensationListItemDto
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public string DeliveryBoyPhone { get; set; } = string.Empty;
        public string? RouteName { get; set; }
        public string? RouteCode { get; set; }
        public byte Month { get; set; }
        public short Year { get; set; }
        public string CompensationType { get; set; } = string.Empty;
        public decimal EligibleCompletedLitres { get; set; }
        public decimal ApplicableCommissionRate { get; set; }
        public decimal CalculatedCommission { get; set; }
        public decimal FixedSalaryAmount { get; set; }
        public decimal GrossEarnings => CompensationType == "Fixed Salary" ? FixedSalaryAmount : CalculatedCommission;
        public decimal Adjustments { get; set; }
        public decimal NetPayableAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? FinalizedAt { get; set; }
        public string? FinalizedByName { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? PaymentReference { get; set; }
        public string? PaidByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
