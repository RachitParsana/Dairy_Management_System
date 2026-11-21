using System;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ExportController : Controller
    {
        private readonly IExportService _exportService;
        private readonly IRouteService _routeService;
        private readonly ILogger<ExportController> _logger;

        public ExportController(
            IExportService exportService,
            IRouteService routeService,
            ILogger<ExportController> logger)
        {
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Centralized Export Management Hub.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct = default)
        {
            try
            {
                var routes = await _routeService.GetActiveRoutesLookupAsync(ct).ConfigureAwait(false);
                ViewBag.Routes = routes;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load routes for Export Hub.");
                ViewBag.Routes = Array.Empty<Dairy_Management_System.Models.Route.RouteLookupModel>();
                return View();
            }
        }

        /// <summary>
        /// Generates and streams Customers Master CSV.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Customers(int? routeId, bool? isActive, CancellationToken ct = default)
        {
            try
            {
                var csvBytes = await _exportService.GenerateCustomersCsvAsync(routeId, isActive, ct).ConfigureAwait(false);
                var fileName = $"customers_master_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                return File(csvBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Customers CSV export.");
                TempData["ErrorMessage"] = "Failed to generate Customers CSV. Please verify parameters.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates and streams Daily Milk Deliveries CSV.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DailyDeliveries(DateOnly? fromDate, DateOnly? toDate, int? routeId, string? shift, CancellationToken ct = default)
        {
            var from = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            var to = toDate ?? DateOnly.FromDateTime(DateTime.Today);

            if (from > to)
            {
                from = to.AddDays(-30);
            }

            try
            {
                var csvBytes = await _exportService.GenerateDailyDeliveriesCsvAsync(from, to, routeId, shift, ct).ConfigureAwait(false);
                var fileName = $"deliveries_export_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
                return File(csvBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Daily Deliveries CSV export.");
                TempData["ErrorMessage"] = "Failed to generate Deliveries CSV. Please verify date parameters.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates and streams Monthly Billing Statements CSV.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> MonthlyBills(byte? month, short? year, string? paymentStatus, int? routeId, CancellationToken ct = default)
        {
            byte m = month ?? (byte)DateTime.Today.Month;
            short y = year ?? (short)DateTime.Today.Year;

            if (m < 1 || m > 12) m = (byte)DateTime.Today.Month;
            if (y < 2020 || y > 2035) y = (short)DateTime.Today.Year;

            try
            {
                var csvBytes = await _exportService.GenerateMonthlyBillsCsvAsync(m, y, paymentStatus, routeId, ct).ConfigureAwait(false);
                var fileName = $"monthly_bills_{m:D2}_{y}.csv";
                return File(csvBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Monthly Bills CSV export.");
                TempData["ErrorMessage"] = "Failed to generate Monthly Bills CSV.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates and streams Payments Ledger CSV.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Payments(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default)
        {
            try
            {
                var csvBytes = await _exportService.GeneratePaymentsCsvAsync(fromDate, toDate, paymentMode, ct).ConfigureAwait(false);
                var fileName = $"payments_ledger_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                return File(csvBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Payments CSV export.");
                TempData["ErrorMessage"] = "Failed to generate Payments CSV.";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates and streams Delivery Boy Monthly Compensation CSV.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Compensation(byte? month, short? year, CancellationToken ct = default)
        {
            byte m = month ?? (byte)DateTime.Today.Month;
            short y = year ?? (short)DateTime.Today.Year;

            if (m < 1 || m > 12) m = (byte)DateTime.Today.Month;
            if (y < 2020 || y > 2035) y = (short)DateTime.Today.Year;

            try
            {
                var csvBytes = await _exportService.GenerateCompensationCsvAsync(m, y, ct).ConfigureAwait(false);
                var fileName = $"compensation_report_{m:D2}_{y}.csv";
                return File(csvBytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Compensation CSV export.");
                TempData["ErrorMessage"] = "Failed to generate Compensation CSV.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
