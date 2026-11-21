using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Reports;
using Dairy_Management_System.Repositories;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    public class ReportsService : IReportsService
    {
        private readonly IReportsRepository _reportsRepository;
        private readonly ILogger<ReportsService> _logger;

        public ReportsService(IReportsRepository reportsRepository, ILogger<ReportsService> logger)
        {
            _reportsRepository = reportsRepository ?? throw new ArgumentNullException(nameof(reportsRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MonthlyBillingVsCollectionTrendDto>> GetBillingVsCollectionTrendsAsync(int months = 6, CancellationToken ct = default)
        {
            if (months < 1) months = 6;
            if (months > 36) months = 36;

            return await _reportsRepository.GetMonthlyBillingVsCollectionsAsync(months, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<RouteMilkConsumptionReportDto>> GetRouteMilkConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? routeId = null, CancellationToken ct = default)
        {
            if (fromDate > toDate)
            {
                throw new ArgumentException("From date cannot be later than To date.", nameof(fromDate));
            }

            return await _reportsRepository.GetRouteMilkConsumptionAsync(fromDate, toDate, routeId, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DailyCategoryShiftConsumptionDto>> GetCategoryShiftConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? categoryId = null, string? shift = null, CancellationToken ct = default)
        {
            if (fromDate > toDate)
            {
                throw new ArgumentException("From date cannot be later than To date.", nameof(fromDate));
            }

            return await _reportsRepository.GetDailyCategoryShiftConsumptionAsync(fromDate, toDate, categoryId, shift, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CustomerBalanceAgingDto>> GetCustomerBalanceAgingAsync(int? routeId = null, CancellationToken ct = default)
        {
            return await _reportsRepository.GetCustomerBalanceAgingAsync(routeId, ct).ConfigureAwait(false);
        }
    }
}
