using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Route;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Administrator controller managing master delivery routes (Create, Edit, Activate/Deactivate).
    /// Eliminates fixed hardcoded route constraints by allowing dynamic route additions across the platform.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class RouteController : Controller
    {
        private readonly IRouteManagementService _routeManagementService;
        private readonly ILogger<RouteController> _logger;

        public RouteController(IRouteManagementService routeManagementService, ILogger<RouteController> logger)
        {
            _routeManagementService = routeManagementService ?? throw new ArgumentNullException(nameof(routeManagementService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(claim, out int userId) || userId <= 0)
            {
                throw new UnauthorizedAccessException("Valid administrator session is required.");
            }
            return userId;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct = default)
        {
            ViewData["ActiveMenu"] = "DeliveryBoy";
            var routes = await _routeManagementService.GetAllRoutesAsync(ct).ConfigureAwait(false);

            var vm = new RouteIndexViewModel
            {
                Routes = routes,
                ActiveRoutes = routes.Count(r => r.IsActive),
                InactiveRoutes = routes.Count(r => !r.IsActive),
                TotalAssignedCustomers = routes.Sum(r => r.ActiveCustomerCount),
                TotalAssignedAgents = routes.Sum(r => r.ActiveDeliveryBoyCount)
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RouteFormModel model, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                TempData["ErrorMessage"] = string.Join(" ", errors);
                return RedirectToAction(nameof(Index));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                var result = await _routeManagementService.CreateRouteAsync(model, adminUserId, ct).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.ErrorMessage ?? "Could not create route.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating route.");
                TempData["ErrorMessage"] = "An unexpected error occurred while creating the route.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// JSON endpoint enabling Delivery Boy and Customer onboarding/edit forms to create new routes inline.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuick([FromBody] RouteFormModel model, CancellationToken ct = default)
        {
            if (model == null || !TryValidateModel(model))
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { success = false, message = string.Join(" ", errors) });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                var result = await _routeManagementService.CreateRouteAsync(model, adminUserId, ct).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return BadRequest(new { success = false, message = result.ErrorMessage ?? "Failed to create route." });
                }

                return Json(new
                {
                    success = true,
                    message = result.Message,
                    route = new
                    {
                        routeId = result.RouteId,
                        routeCode = result.RouteCode,
                        routeName = result.RouteName
                    }
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during inline quick route creation.");
                return StatusCode(500, new { success = false, message = "An unexpected error occurred while creating the route." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(RouteFormModel model, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                TempData["ErrorMessage"] = string.Join(" ", errors);
                return RedirectToAction(nameof(Index));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                var result = await _routeManagementService.UpdateRouteAsync(model, adminUserId, ct).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.ErrorMessage ?? "Could not update route.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating route {RouteId}.", model.RouteId);
                TempData["ErrorMessage"] = "An unexpected error occurred while updating the route.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int routeId, bool activate, CancellationToken ct = default)
        {
            try
            {
                int adminUserId = GetCurrentUserId();
                var result = await _routeManagementService.SetRouteActiveStatusAsync(routeId, activate, adminUserId, ct).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.ErrorMessage ?? "Could not update route status.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error toggling route status for {RouteId}.", routeId);
                TempData["ErrorMessage"] = "An unexpected error occurred while changing route status.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
