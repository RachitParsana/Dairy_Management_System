using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Repositories;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Controller managing all Administrator Customer Management operations,
    /// directory searches, onboarding, profile details, multi-category subscriptions,
    /// atomic schedule transitions, route reassignments, vacations, and deactivation/reactivation.
    /// Strictly authorized for Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class CustomerController : Controller
    {
        private readonly ICustomerService _customerService;
        private readonly ICustomerVacationService _vacationService;
        private readonly ICustomerRepository _customerRepository;
        private readonly ICustomerSubscriptionRepository _subscriptionRepository;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<CustomerController> _logger;

        public CustomerController(
            ICustomerService customerService,
            ICustomerVacationService vacationService,
            ICustomerRepository customerRepository,
            ICustomerSubscriptionRepository subscriptionRepository,
            ISqlConnectionFactory connectionFactory,
            ILogger<CustomerController> logger)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _vacationService = vacationService ?? throw new ArgumentNullException(nameof(vacationService));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
            _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Customer Listing & Continuous Scrolling

        /// <summary>
        /// Displays the customer management directory with search, filters, and statistics.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] CustomerFilterCriteria? criteria, CancellationToken cancellationToken)
        {
            criteria ??= new CustomerFilterCriteria();
            criteria.PageNumber = Math.Max(1, criteria.PageNumber);
            criteria.PageSize = criteria.PageSize <= 0 ? 25 : criteria.PageSize;

            var customers = await _customerService.GetCustomerListAsync(criteria, cancellationToken);
            var totalCount = await _customerService.GetCustomerCountAsync(criteria, cancellationToken);
            var counts = await GetCustomerCountsAsync(cancellationToken);

            var viewModel = new CustomerIndexViewModel
            {
                Criteria = criteria,
                Customers = customers,
                TotalCount = totalCount,
                ActiveCount = counts.Active,
                InactiveCount = counts.Inactive,
                VacationTodayCount = counts.VacationToday,
                Routes = await GetRoutesLookupAsync(cancellationToken),
                DeliveryBoys = await GetDeliveryBoysLookupAsync(cancellationToken),
                Areas = await GetDistinctAreasLookupAsync(cancellationToken),
                Categories = await GetCategoriesLookupAsync(cancellationToken)
            };

            ViewData["ActiveMenu"] = "Customers";
            ViewData["Title"] = "Customer Management";

            return View(viewModel);
        }

        /// <summary>
        /// AJAX continuous scrolling endpoint returning customer records in JSON format.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCustomerListJson([FromQuery] CustomerFilterCriteria criteria, CancellationToken cancellationToken)
        {
            criteria.PageNumber = Math.Max(1, criteria.PageNumber);
            criteria.PageSize = criteria.PageSize <= 0 ? 25 : criteria.PageSize;

            var customers = await _customerService.GetCustomerListAsync(criteria, cancellationToken);
            var totalCount = await _customerService.GetCustomerCountAsync(criteria, cancellationToken);

            bool hasMore = (criteria.PageNumber * criteria.PageSize) < totalCount;

            return Json(new
            {
                success = true,
                data = customers,
                totalCount,
                pageNumber = criteria.PageNumber,
                pageSize = criteria.PageSize,
                hasMore
            });
        }

        #endregion

        #region Onboarding (Add Customer)

        /// <summary>
        /// Displays the structured customer registration form.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var nextCode = await _customerRepository.GenerateNextCustomerCodeAsync(cancellationToken);
            var model = new CreateCustomerViewModel
            {
                DeliveryStartDate = DateOnly.FromDateTime(DateTime.Today),
                DeliverySequence = 1,
                PreferredShift = "Both",
                Subscriptions = new List<SubscriptionLineInputModel>
                {
                    new()
                    {
                        CategoryId = 1,
                        Shift = "Morning",
                        Quantity = 1.0m,
                        DeliverySchedule = "Daily",
                        DeliveryDays = "1,2,3,4,5,6,7"
                    }
                }
            };

            await LoadLookupsIntoViewBagAsync(cancellationToken);
            ViewBag.NextCustomerCode = nextCode;
            ViewData["ActiveMenu"] = "Customers";
            ViewData["Title"] = "Add New Customer";

            return View(model);
        }

        /// <summary>
        /// Submits new customer registration with initial subscriptions and delivery assignment.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCustomerViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                await LoadLookupsIntoViewBagAsync(cancellationToken);
                ViewData["ActiveMenu"] = "Customers";
                ViewData["Title"] = "Add New Customer";
                return View(model);
            }

            int currentUserId = GetCurrentUserId();
            var result = await _customerService.CreateCustomerAsync(model, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                var error = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : (result.Message ?? "Unable to create customer.");
                ModelState.AddModelError(string.Empty, error);
                await LoadLookupsIntoViewBagAsync(cancellationToken);
                ViewData["ActiveMenu"] = "Customers";
                ViewData["Title"] = "Add New Customer";
                return View(model);
            }

            TempData["SuccessMessage"] = $"Customer '{model.FullName}' registered successfully (Code: {result.CustomerCode ?? "Generated"}).";
            _logger.LogInformation("Admin {AdminId} registered new customer {Code} with ID {CustomerId}.", currentUserId, result.CustomerCode, result.CustomerId);

            if (result.CustomerId.HasValue)
            {
                return RedirectToAction(nameof(Details), new { id = result.CustomerId.Value });
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Customer Details

        /// <summary>
        /// Displays complete profile, multi-category subscriptions, custom pricing,
        /// assignment history, and recent vacation logs for a customer.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var details = await _customerService.GetCustomerDetailsAsync(id, cancellationToken);
            if (details == null)
            {
                TempData["ErrorMessage"] = $"Customer ID {id} was not found.";
                return RedirectToAction(nameof(Index));
            }

            // Populate Route Name if empty
            if (string.IsNullOrWhiteSpace(details.RouteName) && details.Customer.RouteId > 0)
            {
                details.RouteName = await GetRouteNameAsync(details.Customer.RouteId, cancellationToken);
            }

            await LoadLookupsIntoViewBagAsync(cancellationToken);
            ViewData["ActiveMenu"] = "Customers";
            ViewData["Title"] = $"Customer Profile — {details.Customer.CustomerCode}";

            return View(details);
        }

        #endregion

        #region Edit Customer

        /// <summary>
        /// Displays the edit form for customer demographics and routing preferences.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByIdAsync(id, cancellationToken);
            if (customer == null)
            {
                TempData["ErrorMessage"] = $"Customer ID {id} was not found.";
                return RedirectToAction(nameof(Index));
            }

            var model = new EditCustomerViewModel
            {
                CustomerId = customer.CustomerId,
                CustomerCode = customer.CustomerCode,
                FullName = customer.FullName,
                FullNameGujarati = customer.FullNameGujarati,
                Phone = customer.Phone,
                AlternatePhone = customer.AlternatePhone,
                Address = customer.Address,
                Area = customer.Area,
                Landmark = customer.Landmark,
                RouteId = customer.RouteId,
                DeliverySequence = customer.DeliverySequence,
                PreferredShift = customer.PreferredShift,
                Notes = customer.Notes,
                Status = customer.Status
            };

            await LoadLookupsIntoViewBagAsync(cancellationToken);
            ViewData["ActiveMenu"] = "Customers";
            ViewData["Title"] = $"Edit Customer — {customer.CustomerCode}";

            return View(model);
        }

        /// <summary>
        /// Updates customer demographic and routing preferences, respecting lifecycle immutability.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EditCustomerViewModel model, CancellationToken cancellationToken)
        {
            if (id != model.CustomerId)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                await LoadLookupsIntoViewBagAsync(cancellationToken);
                ViewData["ActiveMenu"] = "Customers";
                ViewData["Title"] = $"Edit Customer — {model.CustomerCode}";
                return View(model);
            }

            int currentUserId = GetCurrentUserId();
            var result = await _customerService.UpdateCustomerAsync(model, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, GetResultError(result, "Unable to update customer."));
                await LoadLookupsIntoViewBagAsync(cancellationToken);
                ViewData["ActiveMenu"] = "Customers";
                ViewData["Title"] = $"Edit Customer — {model.CustomerCode}";
                return View(model);
            }

            TempData["SuccessMessage"] = $"Customer '{model.FullName}' updated successfully.";
            _logger.LogInformation("Admin {AdminId} updated customer {Code} (ID: {Id}).", currentUserId, model.CustomerCode, id);

            return RedirectToAction(nameof(Details), new { id });
        }

        #endregion

        #region Delivery Boy Assignment & Reassignment

        /// <summary>
        /// Reassigns or unassigns a customer's delivery boy adhering to Day 3 lifecycle invariants.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignDeliveryBoy(CustomerReassignmentViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields for reassignment.";
                return RedirectToAction(nameof(Details), new { id = model.CustomerId });
            }

            int currentUserId = GetCurrentUserId();
            var result = await _customerService.ReassignDeliveryBoyAsync(model, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Reassignment failed.");
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Delivery boy reassigned successfully.";
                _logger.LogInformation("Admin {AdminId} reassigned customer {CustId} to delivery boy {DBId}.", currentUserId, model.CustomerId, model.NewDeliveryBoyId);
            }

            return RedirectToAction(nameof(Details), new { id = model.CustomerId });
        }

        /// <summary>
        /// API check verifying whether a delivery has already been logged for a customer on a given calendar date.
        /// Used by confirmation dialogs in reassignment and deactivation modals.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckDeliveryOnDate(int customerId, string date, CancellationToken cancellationToken)
        {
            if (!DateOnly.TryParse(date, out var targetDate))
            {
                return Json(new { success = false, message = "Invalid date format." });
            }

            bool hasDelivery = await _customerRepository.HasDeliveryOnDateAsync(customerId, targetDate, cancellationToken);
            return Json(new { success = true, hasDelivery, date = targetDate.ToString("yyyy-MM-dd") });
        }

        #endregion

        #region Subscription Management & Schedule Transitions

        /// <summary>
        /// Adds a new multi-category subscription line item for a customer.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfigureSubscription(int customerId, SubscriptionLineInputModel subscriptionInput, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _customerService.ConfigureSubscriptionAsync(customerId, subscriptionInput, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to configure subscription line.");
            }
            else
            {
                TempData["SuccessMessage"] = "Product subscription line added successfully.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Atomically updates a customer's delivery schedule using the approved backend stored procedure
        /// dbo.usp_CustomerSubscription_UpdateSchedule, preserving parent/child consistency.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSubscriptionSchedule(int subscriptionId, int customerId, string newSchedule, string? newDays, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            try
            {
                await _subscriptionRepository.UpdateSubscriptionScheduleAsync(subscriptionId, newSchedule, newDays, currentUserId, cancellationToken);
                TempData["SuccessMessage"] = $"Delivery schedule updated successfully to '{newSchedule}'.";
                _logger.LogInformation("Admin {AdminId} updated schedule for subscription {SubId} to {Schedule} ({Days}).", currentUserId, subscriptionId, newSchedule, newDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update subscription schedule for SubId {SubId}.", subscriptionId);
                TempData["ErrorMessage"] = ex.Message.Contains("Schedule") ? ex.Message : "Failed to update delivery schedule: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Ends (pauses) an individual subscription line starting on a designated effective date.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndSubscription(int subscriptionId, int customerId, string? effectiveToDate, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            DateOnly effectiveTo = DateOnly.TryParse(effectiveToDate, out var dt) ? dt : DateOnly.FromDateTime(DateTime.Today);

            try
            {
                await _subscriptionRepository.EndSubscriptionAsync(subscriptionId, effectiveTo, currentUserId, cancellationToken);
                TempData["SuccessMessage"] = "Subscription line ended successfully.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to end subscription: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Sets a custom pricing agreement for a customer and category.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCustomPrice(int customerId, int categoryId, decimal customPrice, string effectiveFromDate, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            DateOnly effectiveFrom = DateOnly.TryParse(effectiveFromDate, out var dt) ? dt : DateOnly.FromDateTime(DateTime.Today);

            var result = await _customerService.SetCustomPriceAsync(customerId, categoryId, customPrice, effectiveFrom, currentUserId, cancellationToken);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to save custom price.");
            }
            else
            {
                TempData["SuccessMessage"] = "Custom price agreement saved successfully.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Ends a custom pricing agreement starting on a designated effective date.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndCustomPrice(int pricingId, int customerId, string? effectiveToDate, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            DateOnly effectiveTo = DateOnly.TryParse(effectiveToDate, out var dt) ? dt : DateOnly.FromDateTime(DateTime.Today);

            try
            {
                await _subscriptionRepository.EndCustomPriceAsync(pricingId, effectiveTo, currentUserId, cancellationToken);
                TempData["SuccessMessage"] = "Custom pricing agreement ended.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to end custom price: " + ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        #endregion

        #region Deactivation & Reactivation

        /// <summary>
        /// Deactivates a customer with explicit effective date boundaries and recorded delivery confirmation.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(DeactivateCustomerViewModel model, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _customerService.DeactivateCustomerAsync(model, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Customer deactivation failed.");
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Customer deactivated successfully.";
                _logger.LogInformation("Admin {AdminId} deactivated customer {CustId} effective {Date}.", currentUserId, model.CustomerId, model.DeactivationDate);
            }

            return RedirectToAction(nameof(Details), new { id = model.CustomerId });
        }

        /// <summary>
        /// Reactivates an inactive customer by assigning a new route, sequence, delivery boy, and fresh subscriptions.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(ReactivateCustomerViewModel model, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _customerService.ReactivateCustomerAsync(model, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Customer reactivation failed.");
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Customer reactivated successfully.";
                _logger.LogInformation("Admin {AdminId} reactivated customer {CustId} effective {Date}.", currentUserId, model.CustomerId, model.ReactivationDate);
            }

            return RedirectToAction(nameof(Details), new { id = model.CustomerId });
        }

        #endregion

        #region Vacation Management

        /// <summary>
        /// Submits a customer vacation request. When initiated by Admin, automatically approves it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestVacation(CustomerVacationViewModel model, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _vacationService.RequestVacationAsync(model, currentUserId, autoApproveIfAdmin: true, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to register vacation request.");
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Vacation registered and approved.";
                _logger.LogInformation("Admin {AdminId} recorded vacation for customer {CustId} ({Start} to {End}).", currentUserId, model.CustomerId, model.StartDate, model.EndDate);
            }

            return RedirectToAction(nameof(Details), new { id = model.CustomerId });
        }

        /// <summary>
        /// Approves a pending vacation request.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveVacation(int vacationId, int customerId, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _vacationService.ApproveVacationAsync(vacationId, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to approve vacation.");
            }
            else
            {
                TempData["SuccessMessage"] = "Vacation request approved. Applicable delivery shifts are locked.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Rejects a pending vacation request with reason.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectVacation(int vacationId, int customerId, string rejectionReason, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _vacationService.RejectVacationAsync(vacationId, rejectionReason, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to reject vacation.");
            }
            else
            {
                TempData["SuccessMessage"] = "Vacation request rejected.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Resumes customer deliveries early from an active approved vacation.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeEarlyVacation(int vacationId, int customerId, string resumedDate, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            if (!DateOnly.TryParse(resumedDate, out var date))
            {
                TempData["ErrorMessage"] = "Invalid early resumption date format.";
                return RedirectToAction(nameof(Details), new { id = customerId });
            }

            var result = await _vacationService.ResumeEarlyVacationAsync(vacationId, date, currentUserId, cancellationToken);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to resume vacation early.");
            }
            else
            {
                TempData["SuccessMessage"] = $"Early resumption confirmed. Deliveries resume on {date:MMM dd, yyyy}.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        /// <summary>
        /// Cancels a vacation record.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelVacation(int vacationId, int customerId, CancellationToken cancellationToken)
        {
            int currentUserId = GetCurrentUserId();
            var result = await _vacationService.CancelVacationAsync(vacationId, currentUserId, cancellationToken);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = GetResultError(result, "Failed to cancel vacation.");
            }
            else
            {
                TempData["SuccessMessage"] = "Vacation cancelled successfully.";
            }

            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        #endregion

        #region Helper Methods

        private static string GetResultError(CustomerOperationResult result, string fallback = "Operation failed.")
        {
            return result.Errors.Count > 0 ? string.Join("; ", result.Errors) : (result.Message ?? fallback);
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idClaim, out var id) ? id : 1;
        }

        private async Task LoadLookupsIntoViewBagAsync(CancellationToken cancellationToken)
        {
            ViewBag.Routes = await GetRoutesLookupAsync(cancellationToken);
            ViewBag.DeliveryBoys = await GetDeliveryBoysLookupAsync(cancellationToken);
            ViewBag.Categories = await GetCategoriesLookupAsync(cancellationToken);
            ViewBag.Areas = await GetDistinctAreasLookupAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<RouteLookupItem>> GetRoutesLookupAsync(CancellationToken cancellationToken)
        {
            var list = new List<RouteLookupItem>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = "SELECT RouteId, RouteCode, RouteName FROM Routes WHERE IsActive = 1 ORDER BY RouteName;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new RouteLookupItem
                {
                    RouteId = reader.GetInt32(0),
                    RouteCode = reader.GetString(1),
                    RouteName = reader.GetString(2)
                });
            }
            return list;
        }

        private async Task<string> GetRouteNameAsync(int routeId, CancellationToken cancellationToken)
        {
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = "SELECT RouteName FROM Routes WHERE RouteId = @RouteId;";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RouteId", routeId);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result?.ToString() ?? string.Empty;
        }

        private async Task<IReadOnlyList<DeliveryBoyLookupItem>> GetDeliveryBoysLookupAsync(CancellationToken cancellationToken)
        {
            var list = new List<DeliveryBoyLookupItem>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT db.DeliveryBoyId, u.FullName, u.Phone, db.VehicleNumber, db.IsActive
                FROM DeliveryBoys db
                INNER JOIN Users u ON db.UserId = u.UserId
                WHERE db.IsActive = 1
                ORDER BY u.FullName;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new DeliveryBoyLookupItem
                {
                    DeliveryBoyId = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
                    VehicleNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsActive = reader.GetBoolean(4)
                });
            }
            return list;
        }

        private async Task<IReadOnlyList<MilkCategoryLookupItem>> GetCategoriesLookupAsync(CancellationToken cancellationToken)
        {
            var list = new List<MilkCategoryLookupItem>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT CategoryId, CategoryCode, CategoryName, DefaultPrice, UnitOfMeasure, ProductType, IsActive
                FROM MilkCategories
                WHERE IsActive = 1
                ORDER BY DisplayOrder, CategoryName;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new MilkCategoryLookupItem
                {
                    CategoryId = reader.GetInt32(0),
                    CategoryCode = reader.GetString(1),
                    CategoryName = reader.GetString(2),
                    DefaultPrice = reader.GetDecimal(3),
                    UnitOfMeasure = reader.GetString(4),
                    ProductType = reader.GetString(5),
                    IsActive = reader.GetBoolean(6)
                });
            }
            return list;
        }

        private async Task<IReadOnlyList<string>> GetDistinctAreasLookupAsync(CancellationToken cancellationToken)
        {
            var list = new List<string>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = "SELECT DISTINCT Area FROM Customers WHERE Area IS NOT NULL AND LTRIM(RTRIM(Area)) != '' ORDER BY Area;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        private async Task<(int Total, int Active, int Inactive, int VacationToday)> GetCustomerCountsAsync(CancellationToken cancellationToken)
        {
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = @"
                WITH ActiveVacationToday AS (
                    SELECT DISTINCT CustomerId
                    FROM CustomerVacations
                    WHERE Status = 'Approved'
                      AND StartDate <= CAST(GETDATE() AS DATE)
                      AND EndDate >= CAST(GETDATE() AS DATE)
                      AND (ResumedDate IS NULL OR CAST(GETDATE() AS DATE) < ResumedDate)
                )
                SELECT
                    COUNT(1) AS TotalCount,
                    ISNULL(SUM(CASE WHEN c.IsActive = 1 AND c.Status = 'Active' THEN 1 ELSE 0 END), 0) AS ActiveCount,
                    ISNULL(SUM(CASE WHEN c.IsActive = 0 OR c.Status = 'Inactive' THEN 1 ELSE 0 END), 0) AS InactiveCount,
                    COUNT(v.CustomerId) AS VacationTodayCount
                FROM Customers c
                LEFT JOIN ActiveVacationToday v ON c.CustomerId = v.CustomerId;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                int total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                int active = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                int inactive = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                int vacation = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                return (total, active, inactive, vacation);
            }
            return (0, 0, 0, 0);
        }

        #endregion
    }
}
