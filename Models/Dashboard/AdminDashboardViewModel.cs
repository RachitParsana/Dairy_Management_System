using System;
using System.Collections.Generic;
using Dairy_Management_System.Models.Notification;

namespace Dairy_Management_System.Models.Dashboard
{
    public class AdminDashboardViewModel
    {
        // High-Level Operational & Master Metrics
        public int TotalCustomers { get; set; }
        public int ActiveCustomers { get; set; }
        public int ActiveDeliveryBoys { get; set; }

        // Today's Deliveries Rollup
        public decimal TodayDeliveredLitres { get; set; }
        public int TodayCompletedDrops { get; set; }
        public int TodayPendingDrops { get; set; }

        // Current Month Financial Aggregations
        public decimal CurrentMonthBillingTotal { get; set; }
        public decimal CurrentMonthCollectedTotal { get; set; }
        public decimal TotalOutstandingBalance { get; set; }
        public int UnpaidInvoicesCount { get; set; }

        // Approved Month-Based Ageing Buckets (from unpaid invoice portions)
        public decimal UnpaidBalance1Month { get; set; }
        public decimal UnpaidBalance2Months { get; set; }
        public decimal UnpaidBalance3Months { get; set; }
        public decimal UnpaidBalance4To5Months { get; set; }
        public decimal UnpaidBalance6PlusMonths { get; set; }

        // Collections Efficiency
        public decimal CurrentMonthCollectionEfficiencyPercent =>
            CurrentMonthBillingTotal > 0
                ? Math.Round((CurrentMonthCollectedTotal / CurrentMonthBillingTotal) * 100, 2)
                : 0m;

        // Today's Shift Breakdown
        public List<TodayDeliveryShiftBreakdownDto> ShiftBreakdown { get; set; } = new();

        // Recent Operational Deliveries Ledger (Top 7 rows)
        public List<RecentOperationalDeliveryItemDto> RecentDeliveries { get; set; } = new();
    }

    public class TodayDeliveryShiftBreakdownDto
    {
        public string Shift { get; set; } = string.Empty;
        public decimal DeliveredLitres { get; set; }
        public int CompletedDeliveries { get; set; }
        public int PendingDeliveries { get; set; }
    }

    public class RecentOperationalDeliveryItemDto
    {
        public long DeliveryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal TotalAmount { get; set; }
        public string DeliveryStatus { get; set; } = string.Empty;
        public string PrimaryCategoryName { get; set; } = string.Empty;
    }
}
