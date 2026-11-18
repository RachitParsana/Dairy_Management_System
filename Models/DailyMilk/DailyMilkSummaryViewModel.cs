using System;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Aggregated summary statistics for daily milk deliveries across a date, shift, route, or delivery boy.
    /// </summary>
    public class DailyMilkSummaryViewModel
    {
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = "Morning";
        public int? RouteId { get; set; }
        public string? RouteName { get; set; }
        public int? DeliveryBoyId { get; set; }
        public string? DeliveryBoyName { get; set; }

        public int TotalCustomers { get; set; }
        public int CompletedDeliveries { get; set; }
        public int PendingDeliveries { get; set; }
        public int NotDeliveredCount { get; set; }
        public int VacationCount { get; set; }

        /// <summary>
        /// Total liquid milk volume in litres successfully delivered (Completed entries).
        /// </summary>
        public decimal TotalLiquidLitres { get; set; }

        /// <summary>
        /// Total liquid litres qualifying for delivery boy commission.
        /// </summary>
        public decimal EligibleCommissionLitres { get; set; }

        /// <summary>
        /// Total monetary value of completed deliveries.
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Delivery completion percentage (CompletedDeliveries / TotalCustomers * 100).
        /// </summary>
        public decimal CompletionPercentage => TotalCustomers > 0
            ? Math.Round((decimal)CompletedDeliveries / TotalCustomers * 100m, 1)
            : 0m;
    }
}
