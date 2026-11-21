using System;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Reports;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ReportsController : Controller
    {
        private readonly IReportsService _reportsService;
        private readonly IRouteService _routeService;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            IReportsService reportsService,
            IRouteService routeService,
            ILogger<ReportsController> logger)
        {
            _reportsService = reportsService ?? throw new ArgumentNullException(nameof(reportsService));
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Centralized Reports & Analytics Hub.
        /// Displays 6-month billing trends, route consumption comparisons, and customer balance aging overview.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(int months = 6, CancellationToken ct = default)
        {
            if (months < 1 || months > 12) months = 6;

            try
            {
                var trendsTask = _reportsService.GetBillingVsCollectionTrendsAsync(months, ct);
                var routeTask = _reportsService.GetRouteMilkConsumptionAsync(
                    DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
                    DateOnly.FromDateTime(DateTime.Today),
                    null, ct);
                var agingTask = _reportsService.GetCustomerBalanceAgingAsync(null, ct);
                var routesTask = _routeService.GetActiveRoutesLookupAsync(ct);

                await Task.WhenAll(trendsTask, routeTask, agingTask, routesTask).ConfigureAwait(false);

                var vm = new ReportsHubViewModel
                {
                    Months = months,
                    MonthlyTrends = await trendsTask,
                    RouteConsumptions = await routeTask,
                    AgingSummaries = await agingTask,
                    Routes = await routesTask
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Reports Hub analytics.");
                return View(new ReportsHubViewModel());
            }
        }

        /// <summary>
        /// Detailed Daily Milk Category and Shift Consumption Report.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DailyMilk(DateOnly? fromDate, DateOnly? toDate, int? categoryId, string? shift, CancellationToken ct = default)
        {
            var from = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
            var to = toDate ?? DateOnly.FromDateTime(DateTime.Today);

            if (from > to)
            {
                from = to.AddDays(-7);
            }

            try
            {
                var rows = await _reportsService.GetCategoryShiftConsumptionAsync(from, to, categoryId, shift, ct).ConfigureAwait(false);

                var vm = new DailyMilkReportFilterViewModel
                {
                    FromDate = from,
                    ToDate = to,
                    CategoryId = categoryId,
                    Shift = shift,
                    Rows = rows
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Daily Milk consumption report.");
                return View(new DailyMilkReportFilterViewModel { FromDate = from, ToDate = to });
            }
        }

        /// <summary>
        /// Approved Month-Based Customer Balance Ageing Register.
        /// Breaks down unpaid invoices into canonical buckets: 1 Mo (Current), 2 Mo, 3 Mo, 4-5 Mo, 6+ Mo.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CustomerAging(int? routeId, CancellationToken ct = default)
        {
            try
            {
                var routesTask = _routeService.GetActiveRoutesLookupAsync(ct);
                var agingTask = _reportsService.GetCustomerBalanceAgingAsync(routeId, ct);

                await Task.WhenAll(routesTask, agingTask).ConfigureAwait(false);

                var vm = new CustomerAgingReportViewModel
                {
                    SelectedRouteId = routeId,
                    Routes = await routesTask,
                    Rows = await agingTask
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Customer Balance Aging report.");
                return View(new CustomerAgingReportViewModel());
            }
        }
    }
}
