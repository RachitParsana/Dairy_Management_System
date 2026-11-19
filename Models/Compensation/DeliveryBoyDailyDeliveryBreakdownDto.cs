using System;

namespace Dairy_Management_System.Models.Compensation
{
    public class DeliveryBoyDailyDeliveryBreakdownDto
    {
        public long DeliveryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal EligibleLitreQuantity { get; set; }
        public decimal CommissionRateSnapshot { get; set; }
        public decimal EarnedCommission { get; set; }
        public decimal CustomerChargedAmount { get; set; }
    }
}
