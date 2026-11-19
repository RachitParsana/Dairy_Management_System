using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DailyMilk;
using Dairy_Management_System.Models.DeliveryBoy;
using Dairy_Management_System.Models.Route;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Administrator controller managing Delivery Boy onboarding, profile updates,
    /// route assignments, commission rate tiers, and account lifecycle transitions.
    /// Strictly restricted to Admin users.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class DeliveryBoyController : Controller
    {
        private readonly IDeliveryBoyService _deliveryBoyService;
        private readonly ICompensationService _compensationService;
        private readonly IRouteService _routeService;
        private readonly IDailyMilkService _dailyMilkService;
        private readonly ILogger<DeliveryBoyController> _logger;

        public DeliveryBoyController(
            IDeliveryBoyService deliveryBoyService,
            ICompensationService compensationService,
            IRouteService routeService,
            IDailyMilkService dailyMilkService,
            ILogger<DeliveryBoyController> logger)
        {
            _deliveryBoyService = deliveryBoyService ?? throw new ArgumentNullException(nameof(deliveryBoyService));
            _compensationService = compensationService ?? throw new ArgumentNullException(nameof(compensationService));
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _dailyMilkService = dailyMilkService ?? throw new ArgumentNullException(nameof(dailyMilkService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Claims Identity

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var id) && id > 0)
            {
                return id;
            }

            _logger.LogError("Admin identity claim is missing or invalid. User cannot be identified for audit trail.");
            throw new UnauthorizedAccessException("Administrator identity could not be verified. Please log in again.");
        }

        #endregion

        #region 1. Delivery Boy Directory

        [HttpGet]
        public async Task<IActionResult> Index(
            string? searchTerm,
            string? statusFilter,
            string? compensationType,
            int? routeId,
            int page = 1,
            int pageSize = 10,
            CancellationToken ct = default)
        {
            try
            {
                var criteria = new DeliveryBoyFilterCriteria
                {
                    SearchTerm = searchTerm?.Trim(),
                    StatusFilter = string.IsNullOrWhiteSpace(statusFilter) ? "All" : statusFilter.Trim(),
                    CompensationType = string.IsNullOrWhiteSpace(compensationType) ? "All" : compensationType.Trim(),
                    RouteId = routeId
                };

                // Authoritative System-wide Query (unfiltered by search/status/route)
                // This ensures KPI cards always represent the true organizational state
                var systemEmployees = await _deliveryBoyService.GetDirectoryAsync(new DeliveryBoyFilterCriteria
                {
                    SearchTerm = null,
                    StatusFilter = "All",
                    CompensationType = "All",
                    RouteId = null
                }, ct);

                int totalEmployees = systemEmployees.Count;
                int activeEmployees = systemEmployees.Count(b => b.IsActive);
                int inactiveEmployees = systemEmployees.Count(b => !b.IsActive);
                int commissionEmployees = systemEmployees.Count(b => b.CompensationType == "Commission");
                int fixedSalaryEmployees = systemEmployees.Count(b => b.CompensationType == "Fixed Salary");

                // Filtered Matching Query for Directory Grid & Pagination
                bool isFiltered = !string.IsNullOrWhiteSpace(criteria.SearchTerm) ||
                                  (criteria.StatusFilter != "All" && !string.IsNullOrWhiteSpace(criteria.StatusFilter)) ||
                                  (criteria.CompensationType != "All" && !string.IsNullOrWhiteSpace(criteria.CompensationType)) ||
                                  criteria.RouteId.HasValue;

                var matchingEmployees = isFiltered
                    ? await _deliveryBoyService.GetDirectoryAsync(criteria, ct)
                    : systemEmployees;

                int matchingCount = matchingEmployees.Count;

                // Safe Pagination
                int safePage = page < 1 ? 1 : page;
                int safePageSize = pageSize < 1 ? 10 : pageSize;
                var pagedList = matchingEmployees
                    .Skip((safePage - 1) * safePageSize)
                    .Take(safePageSize)
                    .ToList();

                var routes = await _routeService.GetActiveRoutesLookupAsync(ct);

                var viewModel = new DeliveryBoyIndexViewModel
                {
                    Criteria = criteria,
                    DeliveryBoys = pagedList,
                    AvailableRoutes = routes,
                    TotalEmployees = totalEmployees,
                    ActiveEmployees = activeEmployees,
                    InactiveEmployees = inactiveEmployees,
                    CommissionEmployees = commissionEmployees,
                    FixedSalaryEmployees = fixedSalaryEmployees,
                    TotalCount = matchingCount,
                    CurrentPage = safePage,
                    PageSize = safePageSize
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading delivery boy directory");
                TempData["ErrorMessage"] = "An unexpected error occurred while loading the delivery boy directory. Please try again.";
                return View(new DeliveryBoyIndexViewModel());
            }
        }

        #endregion

        #region 2. Register Delivery Boy

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken ct = default)
        {
            try
            {
                var routes = await _routeService.GetActiveRoutesLookupAsync(ct);
                var model = new CreateDeliveryBoyViewModel
                {
                    AvailableRoutes = routes,
                    CompensationType = "Commission",
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.Today)
                };
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Create delivery boy page");
                TempData["ErrorMessage"] = "Unable to load registration page. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateDeliveryBoyViewModel model, CancellationToken ct = default)
        {
            // Conditional validation based on compensation type
            if (model.CompensationType == "Commission")
            {
                model.MonthlyBaseSalary = 0.00m;
                if (model.InitialCommissionRate <= 0.00m)
                {
                    ModelState.AddModelError(nameof(model.InitialCommissionRate), "Initial commission rate per litre must be greater than zero for Commission employees.");
                }
                if (!model.EffectiveFrom.HasValue)
                {
                    ModelState.AddModelError(nameof(model.EffectiveFrom), "Effective-from date is required for commission-based delivery boys.");
                }
            }
            else if (model.CompensationType == "Fixed Salary")
            {
                model.InitialCommissionRate = 0.00m;
                model.EffectiveFrom = null;
                if (model.MonthlyBaseSalary < 0.00m)
                {
                    ModelState.AddModelError(nameof(model.MonthlyBaseSalary), "Monthly base salary cannot be negative.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(model.CompensationType), "Invalid compensation type selected. Must be 'Commission' or 'Fixed Salary'.");
            }

            if (!ModelState.IsValid)
            {
                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                int newDeliveryBoyId = await _deliveryBoyService.CreateDeliveryBoyAsync(model, adminUserId, ct);

                TempData["SuccessMessage"] = $"Delivery boy '{model.FullName}' registered successfully (ID #{newDeliveryBoyId}).";
                return RedirectToAction(nameof(Details), new { id = newDeliveryBoyId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during Create delivery boy");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error registering delivery boy: {Username}", model.Username);
                switch (ex.Number)
                {
                    case 50041:
                        ModelState.AddModelError(nameof(model.Phone), "The phone number is already registered to another staff member.");
                        break;
                    case 50042:
                        ModelState.AddModelError(string.Empty, "Invalid compensation parameters. Please check salary or rate values.");
                        break;
                    case 50043:
                        ModelState.AddModelError(nameof(model.PrimaryRouteId), "The selected primary route does not exist or is inactive.");
                        break;
                    case 2627:
                    case 2601:
                        ModelState.AddModelError(nameof(model.Username), "This username is already taken. Please choose another.");
                        break;
                    default:
                        ModelState.AddModelError(string.Empty, "A database error occurred while creating the account. Please verify input data and retry.");
                        break;
                }

                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error registering delivery boy");
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while creating the delivery boy account. Please try again.");
                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }
        }

        #endregion

        #region 3. Delivery Boy Details

        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Invalid delivery boy ID.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var details = await _deliveryBoyService.GetDetailsAsync(id, ct);
                if (details == null)
                {
                    TempData["ErrorMessage"] = $"Delivery boy #{id} was not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Provide active delivery boys for deactivation customer reassignment modal
                ViewBag.ActiveDeliveryBoys = (await _deliveryBoyService.GetActiveLookupAsync(ct))
                    .Where(b => b.DeliveryBoyId != id)
                    .ToList();

                return View(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving delivery boy details for ID {DeliveryBoyId}", id);
                TempData["ErrorMessage"] = "Unable to load delivery boy details. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion

        #region 4. Edit Delivery Boy

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Invalid delivery boy ID.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var model = await _deliveryBoyService.GetForEditAsync(id, ct);
                if (model == null)
                {
                    TempData["ErrorMessage"] = $"Delivery boy #{id} was not found.";
                    return RedirectToAction(nameof(Index));
                }

                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading edit delivery boy page for ID {DeliveryBoyId}", id);
                TempData["ErrorMessage"] = "Unable to load edit profile page. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditDeliveryBoyViewModel model, CancellationToken ct = default)
        {
            if (model.CompensationType == "Fixed Salary" && model.MonthlyBaseSalary < 0.00m)
            {
                ModelState.AddModelError(nameof(model.MonthlyBaseSalary), "Monthly base salary cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _deliveryBoyService.UpdateProfileAsync(model, adminUserId, ct);
                if (!success)
                {
                    TempData["ErrorMessage"] = "Failed to update profile. Delivery boy may not exist.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["SuccessMessage"] = $"Profile for '{model.FullName}' updated successfully.";
                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during Edit delivery boy");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error updating delivery boy {DeliveryBoyId}", model.DeliveryBoyId);
                if (ex.Number == 50044)
                {
                    ModelState.AddModelError(nameof(model.Phone), "The phone number is already in use by another user.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "A database error occurred while updating the profile. Please try again.");
                }

                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating delivery boy {DeliveryBoyId}", model.DeliveryBoyId);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again.");
                model.AvailableRoutes = await _routeService.GetActiveRoutesLookupAsync(ct);
                return View(model);
            }
        }

        #endregion

        #region 5. Commission Rate Tier Management

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCommissionRate(
            int deliveryBoyId,
            decimal commissionRate,
            DateOnly effectiveFrom,
            string? notes,
            CancellationToken ct = default)
        {
            if (deliveryBoyId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid delivery boy ID.";
                return RedirectToAction(nameof(Index));
            }

            if (commissionRate <= 0.00m)
            {
                TempData["ErrorMessage"] = "Commission rate must be greater than zero.";
                return RedirectToAction(nameof(Details), new { id = deliveryBoyId });
            }

            try
            {
                var details = await _deliveryBoyService.GetDetailsAsync(deliveryBoyId, ct);
                if (details == null)
                {
                    TempData["ErrorMessage"] = $"Delivery boy #{deliveryBoyId} was not found.";
                    return RedirectToAction(nameof(Index));
                }

                if (details.CompensationType != "Commission")
                {
                    TempData["ErrorMessage"] = "Commission rate tiers can only be configured for Commission-based employees.";
                    return RedirectToAction(nameof(Details), new { id = deliveryBoyId });
                }

                int adminUserId = GetCurrentUserId();

                var rateModel = new CreateDeliveryBoyCommissionRateViewModel
                {
                    DeliveryBoyId = deliveryBoyId,
                    CommissionRate = commissionRate,
                    EffectiveFrom = effectiveFrom,
                    Notes = notes?.Trim()
                };

                var result = await _dailyMilkService.AddCommissionRateAsync(rateModel, adminUserId, ct);

                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"New commission rate tier of ₹{commissionRate:N2}/Litre effective from {effectiveFrom:dd-MMM-yyyy} configured successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : (result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Failed to add commission rate tier.");
                }

                return RedirectToAction(nameof(Details), new { id = deliveryBoyId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during AddCommissionRate");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating commission rate tier for DeliveryBoyId {DeliveryBoyId}", deliveryBoyId);
                TempData["ErrorMessage"] = "An unexpected error occurred while adding the commission rate tier.";
                return RedirectToAction(nameof(Details), new { id = deliveryBoyId });
            }
        }

        #endregion

        #region 6. Deactivate & Reactivate

        [HttpGet]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct = default)
        {
            if (id <= 0) return RedirectToAction(nameof(Index));

            var details = await _deliveryBoyService.GetDetailsAsync(id, ct);
            if (details == null || !details.IsActive)
            {
                TempData["ErrorMessage"] = "Delivery boy not found or already inactive.";
                return RedirectToAction(nameof(Index));
            }

            var model = new DeactivateDeliveryBoyViewModel
            {
                DeliveryBoyId = details.DeliveryBoyId,
                FullName = details.FullName,
                ActiveCustomerCount = details.AssignedCustomers.Count(c => c.AssignmentIsActive),
                AvailableDeliveryBoys = (await _deliveryBoyService.GetActiveLookupAsync(ct))
                    .Where(b => b.DeliveryBoyId != id)
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(DeactivateDeliveryBoyViewModel model, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                model.AvailableDeliveryBoys = (await _deliveryBoyService.GetActiveLookupAsync(ct))
                    .Where(b => b.DeliveryBoyId != model.DeliveryBoyId)
                    .ToList();
                return View(model);
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _deliveryBoyService.DeactivateAsync(
                    model.DeliveryBoyId,
                    model.ReassignToDeliveryBoyId,
                    model.Reason,
                    adminUserId,
                    ct);

                if (success)
                {
                    TempData["SuccessMessage"] = $"Delivery boy #{model.DeliveryBoyId} ({model.FullName}) has been deactivated.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Could not deactivate delivery boy.";
                }

                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during Deactivate delivery boy");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error deactivating delivery boy {DeliveryBoyId}", model.DeliveryBoyId);
                TempData["ErrorMessage"] = ex.Number == 50053
                    ? "Delivery boy is already deactivated."
                    : "Database error occurred during deactivation. Please try again.";
                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deactivating delivery boy {DeliveryBoyId}", model.DeliveryBoyId);
                TempData["ErrorMessage"] = "An unexpected error occurred. Please try again.";
                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Reactivate(int id, CancellationToken ct = default)
        {
            if (id <= 0) return RedirectToAction(nameof(Index));

            var details = await _deliveryBoyService.GetDetailsAsync(id, ct);
            if (details == null || details.IsActive)
            {
                TempData["ErrorMessage"] = "Delivery boy not found or already active.";
                return RedirectToAction(nameof(Index));
            }

            var model = new ReactivateDeliveryBoyViewModel
            {
                DeliveryBoyId = details.DeliveryBoyId,
                FullName = details.FullName,
                CompensationType = details.CompensationType,
                NewCommissionRate = details.CompensationType == "Commission" ? details.CurrentCommissionRate : null
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(ReactivateDeliveryBoyViewModel model, CancellationToken ct = default)
        {
            if (model.CompensationType == "Commission" && (!model.NewCommissionRate.HasValue || model.NewCommissionRate.Value <= 0.00m))
            {
                ModelState.AddModelError(nameof(model.NewCommissionRate), "Reactivating a commission-based employee strictly requires an approved commission rate per litre greater than zero.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _deliveryBoyService.ReactivateAsync(
                    model.DeliveryBoyId,
                    model.NewCommissionRate,
                    adminUserId,
                    ct);

                if (success)
                {
                    TempData["SuccessMessage"] = $"Delivery boy #{model.DeliveryBoyId} ({model.FullName}) has been reactivated.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Could not reactivate delivery boy.";
                }

                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during Reactivate delivery boy");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reactivating delivery boy {DeliveryBoyId}", model.DeliveryBoyId);
                TempData["ErrorMessage"] = "An unexpected error occurred during reactivation.";
                return RedirectToAction(nameof(Details), new { id = model.DeliveryBoyId });
            }
        }

        #endregion

        #region 7. Compensation Shortcut

        [HttpGet]
        public IActionResult Compensation(int? id)
        {
            return RedirectToAction("Index", "Compensation", new { deliveryBoyId = id });
        }

        #endregion
    }
}
