using System;

namespace Dairy_Management_System.Models.Reports
{
    public class MonthlyBillingVsCollectionTrendDto
    {
        public short BillingYear { get; set; }
        public byte BillingMonth { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal InvoicedAmount { get; set; }
        public decimal CollectedAmount { get; set; }
        public decimal CollectionEfficiencyPercent =>
            InvoicedAmount > 0
                ? Math.Round((CollectedAmount / InvoicedAmount) * 100, 2)
                : 0m;
    }

    public class RouteMilkConsumptionReportDto
    {
        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public int ActiveCustomerCount { get; set; }
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal MorningVolumeLitres { get; set; }
        public decimal EveningVolumeLitres { get; set; }
    }

    public class DailyCategoryShiftConsumptionDto
    {
        public DateOnly EntryDate { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal DeliveredQuantity { get; set; }
        public decimal TotalMonetaryAmount { get; set; }
    }
}
