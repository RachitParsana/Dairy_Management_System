using System;
using System.Collections.Generic;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Models.Reports
{
    public class ReportsHubViewModel
    {
        public int Months { get; set; } = 6;
        public IReadOnlyList<MonthlyBillingVsCollectionTrendDto> MonthlyTrends { get; set; } = Array.Empty<MonthlyBillingVsCollectionTrendDto>();
        public IReadOnlyList<RouteMilkConsumptionReportDto> RouteConsumptions { get; set; } = Array.Empty<RouteMilkConsumptionReportDto>();
        public IReadOnlyList<CustomerBalanceAgingDto> AgingSummaries { get; set; } = Array.Empty<CustomerBalanceAgingDto>();
        public IReadOnlyList<RouteLookupModel> Routes { get; set; } = Array.Empty<RouteLookupModel>();

        // Summary Aggregates
        public decimal TotalInvoicedInPeriod => MonthlyTrends.Sum(t => t.InvoicedAmount);
        public decimal TotalCollectedInPeriod => MonthlyTrends.Sum(t => t.CollectedAmount);
        public decimal AverageCollectionEfficiency => TotalInvoicedInPeriod > 0
            ? Math.Round((TotalCollectedInPeriod / TotalInvoicedInPeriod) * 100, 2)
            : 0m;

        public decimal TotalOutstandingReceivables => AgingSummaries.Sum(a => a.TotalOutstanding);
    }

    public class DailyMilkReportFilterViewModel
    {
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public int? CategoryId { get; set; }
        public string? Shift { get; set; }
        public IReadOnlyList<DailyCategoryShiftConsumptionDto> Rows { get; set; } = Array.Empty<DailyCategoryShiftConsumptionDto>();

        public decimal TotalQuantity => Rows.Sum(r => r.DeliveredQuantity);
        public decimal TotalAmount => Rows.Sum(r => r.TotalMonetaryAmount);
    }

    public class CustomerAgingReportViewModel
    {
        public int? SelectedRouteId { get; set; }
        public IReadOnlyList<RouteLookupModel> Routes { get; set; } = Array.Empty<RouteLookupModel>();
        public IReadOnlyList<CustomerBalanceAgingDto> Rows { get; set; } = Array.Empty<CustomerBalanceAgingDto>();

        // Month-Based Ageing Summary Aggregates
        public decimal Total1Month => Rows.Sum(r => r.UnpaidBalance1Month);
        public decimal Total2Months => Rows.Sum(r => r.UnpaidBalance2Months);
        public decimal Total3Months => Rows.Sum(r => r.UnpaidBalance3Months);
        public decimal Total4To5Months => Rows.Sum(r => r.UnpaidBalance4To5Months);
        public decimal Total6PlusMonths => Rows.Sum(r => r.UnpaidBalance6PlusMonths);
        public decimal GrandTotalOutstanding => Rows.Sum(r => r.TotalOutstanding);
    }
}
