using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Reports;

namespace Dairy_Management_System.Repositories
{
    public interface IReportsRepository
    {
        Task<IReadOnlyList<MonthlyBillingVsCollectionTrendDto>> GetMonthlyBillingVsCollectionsAsync(int months = 6, CancellationToken ct = default);
        Task<IReadOnlyList<RouteMilkConsumptionReportDto>> GetRouteMilkConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? routeId = null, CancellationToken ct = default);
        Task<IReadOnlyList<DailyCategoryShiftConsumptionDto>> GetDailyCategoryShiftConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? categoryId = null, string? shift = null, CancellationToken ct = default);
        Task<IReadOnlyList<CustomerBalanceAgingDto>> GetCustomerBalanceAgingAsync(int? routeId = null, CancellationToken ct = default);
    }
}
