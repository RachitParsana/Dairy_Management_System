using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models;
using Dairy_Management_System.Models.Dashboard;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IDashboardService dashboardService,
            ILogger<HomeController> logger)
        {
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Admin Dashboard endpoint. Strictly protected for staff accounts with Admin role.
        /// Aggregates real-time KPIs, billing vs collections efficiency, and recent deliveries.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            try
            {
                var viewModel = await _dashboardService.GetAdminDashboardViewModelAsync(ct).ConfigureAwait(false);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Admin Dashboard metrics.");
                // Return empty/neutral model gracefully rather than unhandled crash
                return View(new AdminDashboardViewModel());
            }
        }

        /// <summary>
        /// Delivery Boy Portal endpoint. Strictly protected for staff accounts with DeliveryBoy role.
        /// Loads only the authenticated field partner's assigned route, drop metrics, and unread bill alerts.
        /// </summary>
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> Privacy(CancellationToken ct)
        {
            int userId = 0;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(userIdClaim) && int.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            if (userId <= 0)
            {
                _logger.LogWarning("Delivery Boy dashboard accessed without valid NameIdentifier claim.");
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            try
            {
                var viewModel = await _dashboardService.GetDeliveryBoyDashboardViewModelAsync(userId, ct).ConfigureAwait(false);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Delivery Boy Dashboard for UserId: {UserId}", userId);
                return View(new DeliveryBoyDashboardViewModel { FullName = User.Identity?.Name ?? "Partner" });
            }
        }

        /// <summary>
        /// Global status-code aware error endpoint. Safe user-facing errors without leaking SQL or stack traces.
        /// </summary>
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? statusCode = null)
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            var model = new ErrorViewModel
            {
                RequestId = requestId,
                StatusCode = statusCode
            };

            switch (statusCode)
            {
                case 404:
                    model.StatusMessage = "Page or Resource Not Found";
                    model.StatusDescription = "The requested URL or operational resource could not be found. It may have been moved or archived.";
                    break;
                case 403:
                    model.StatusMessage = "Access Forbidden";
                    model.StatusDescription = "You do not have authorization to access this protected area. Please verify your role credentials.";
                    break;
                case 500:
                    model.StatusMessage = "Internal Server Error";
                    model.StatusDescription = "An unexpected error occurred while processing your request. The technical event has been logged for review.";
                    break;
                default:
                    model.StatusMessage = "Operational Notice";
                    model.StatusDescription = "An issue was encountered while processing your request. Please try again or navigate back to your dashboard.";
                    break;
            }

            return View(model);
        }
    }
}
