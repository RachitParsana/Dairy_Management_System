using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DailyMilk;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Controller managing Daily Milk field distribution runsheets, draft saves, and shift finalization
    /// for Delivery Boys, as well as operational monitoring, composite cursor feeds, and audited corrections for Admins.
    /// </summary>
    [Authorize]
    public class DailyMilkController : Controller
    {
        private readonly IDailyMilkService _dailyMilkService;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DailyMilkController> _logger;

        public DailyMilkController(
            IDailyMilkService dailyMilkService,
            ISqlConnectionFactory connectionFactory,
            ILogger<DailyMilkController> logger)
        {
            _dailyMilkService = dailyMilkService ?? throw new ArgumentNullException(nameof(dailyMilkService));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Claims Identity

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idClaim, out var id) ? id : 0;
        }

        #endregion

        #region Delivery Boy Customer Roster

        /// <summary>
        /// Displays the Delivery Boy's assigned active customer roster ordered by delivery sequence.
        /// Exclusively for authenticated DeliveryBoy role, identity resolved from claims.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> MyCustomers(CancellationToken cancellationToken)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("MyCustomers accessed without valid DeliveryBoy claims identity.");
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            try
            {
                var roster = await _dailyMilkService.GetAssignedCustomersRosterAsync(userId, cancellationToken).ConfigureAwait(false);
                return View(roster);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve assigned customers roster for UserId: {UserId}", userId);
                TempData["ErrorMessage"] = "Unable to load assigned customer roster at this time.";
                return View(Array.Empty<Dairy_Management_System.Models.DeliveryBoy.DeliveryBoyCustomerRosterItemViewModel>());
            }
        }

        #endregion

        #region Delivery Boy Runsheet & Entry Portal

        /// <summary>
        /// Displays the Delivery Boy Daily Milk Entry runsheet for the selected date and shift.
        /// Defaults to today and Morning shift. Mobile-first continuous vertical layout.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> Index(DateOnly? entryDate, string? shift, CancellationToken cancellationToken)
        {
            int userId = GetCurrentUserId();
            var targetDate = entryDate ?? DateOnly.FromDateTime(DateTime.Today);
            var targetShift = string.IsNullOrWhiteSpace(shift) ? (DateTime.Now.Hour < 14 ? "Morning" : "Evening") : shift;

            ViewData["Title"] = "Daily Milk Entry";
            ViewData["ActiveMenu"] = "DailyMilkEntry";

            var roster = await _dailyMilkService.GetDeliveryRosterAsync(userId, targetDate, targetShift, cancellationToken);
            roster.AvailableCategories = await GetActiveCategoriesAsync(cancellationToken);
            await EnrichExistingDeliveryItemsAsync(roster, cancellationToken);
            bool subsLoaded = await EnrichSubscribedCategoriesAsync(roster, targetDate, targetShift, cancellationToken);
            if (!subsLoaded)
            {
                ViewData["SubscriptionLoadError"] = "Unable to retrieve customer subscription records due to a system data retrieval error. Submission is temporarily locked to prevent saving incomplete delivery records. Please refresh the page or contact the system administrator.";
            }

            foreach (var cust in roster.Customers)
            {
                if (cust.IsOnApprovedVacation && string.IsNullOrEmpty(cust.VacationWindowText))
                {
                    cust.VacationWindowText = $"{targetShift} Shift - {cust.VacationReason ?? "Approved Vacation"}";
                }
            }

            return View(roster);
        }

        /// <summary>
        /// Saves work-in-progress runsheet entries as a Draft batch without freezing commission snapshots or locking the route.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "DeliveryBoy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDraft([FromBody] DailyMilkSubmissionViewModel model, CancellationToken cancellationToken)
        {
            if (model == null || model.CustomerEntries == null || model.CustomerEntries.Count == 0)
            {
                return Json(new { success = false, message = "No customer delivery entries were provided." });
            }

            int userId = GetCurrentUserId();
            model.IsFinalSubmission = false;

            var result = await _dailyMilkService.SaveDraftAsync(model, userId, cancellationToken);
            return Json(new
            {
                success = result.Succeeded,
                message = result.Message,
                batchId = result.BatchId
            });
        }

        /// <summary>
        /// Finalizes and submits the delivery runsheet for the shift.
        /// Locks the batch, captures effective-dated commission snapshots, and preserves unresolved Pending entries.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "DeliveryBoy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit([FromBody] DailyMilkSubmissionViewModel model, CancellationToken cancellationToken)
        {
            if (model == null || model.CustomerEntries == null || model.CustomerEntries.Count == 0)
            {
                return Json(new { success = false, message = "No customer delivery entries were provided." });
            }

            int userId = GetCurrentUserId();
            model.IsFinalSubmission = true;

            var result = await _dailyMilkService.SubmitDailyDeliveriesAsync(model, userId, cancellationToken);
            return Json(new
            {
                success = result.Succeeded,
                message = result.Message,
                batchId = result.BatchId
            });
        }

        #endregion

        #region Admin Daily Delivery Console & Audited Corrections

        /// <summary>
        /// Displays the Administrator Daily Milk Monitoring and Operational Console.
        /// Supports date, shift, route, delivery boy, status, and unscheduled filters.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminIndex([FromQuery] DailyMilkFilterCriteria? criteria, CancellationToken cancellationToken)
        {
            criteria ??= new DailyMilkFilterCriteria();
            criteria.EntryDate ??= DateOnly.FromDateTime(DateTime.Today);
            criteria.FetchLimit = 25;

            ViewData["Title"] = "Daily Milk Management";
            ViewData["ActiveMenu"] = "DailyMilkManagement";

            // Load lookups for admin filter bar
            ViewBag.DeliveryBoys = await GetDeliveryBoysLookupAsync(cancellationToken);
            ViewBag.Categories = await GetActiveCategoriesAsync(cancellationToken);

            // Fetch summary metrics for selected date & shift
            ViewBag.Summary = await _dailyMilkService.GetDailySummaryAsync(
                criteria.EntryDate.Value,
                criteria.Shift ?? "Morning",
                criteria.DeliveryBoyId,
                cancellationToken);

            // Initial chunk of records
            var initialFeed = await _dailyMilkService.GetDeliveriesContinuousAsync(criteria, cancellationToken);
            ViewBag.FilterCriteria = criteria;

            return View(initialFeed);
        }

        /// <summary>
        /// Streams continuous scroll delivery items using composite cursor continuation (CursorEntryDate, CursorShift, CursorDeliveryId).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> StreamDeliveries([FromQuery] DailyMilkFilterCriteria criteria, CancellationToken cancellationToken)
        {
            criteria.FetchLimit = criteria.FetchLimit <= 0 ? 25 : Math.Min(criteria.FetchLimit, 100);
            var result = await _dailyMilkService.GetDeliveriesContinuousAsync(criteria, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Retrieves detailed delivery information, category items, and audit history for inspection.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetDeliveryDetails(long deliveryId, CancellationToken cancellationToken)
        {
            var delivery = await _dailyMilkService.GetDeliveryDetailsAsync(deliveryId, cancellationToken);
            if (delivery == null)
            {
                return Json(new { success = false, message = "Delivery record not found." });
            }

            return Json(new { success = true, data = delivery });
        }

        /// <summary>
        /// Applies an administrative correction to an existing submitted delivery entry.
        /// Enforces published bill lockout (Error 50030) and mandatory correction justification (min 5 chars).
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorrectDelivery([FromBody] DailyMilkCorrectionViewModel model, CancellationToken cancellationToken)
        {
            if (model == null)
            {
                return Json(new { success = false, message = "Invalid correction payload." });
            }

            if (string.IsNullOrWhiteSpace(model.CorrectionReason) || model.CorrectionReason.Trim().Length < 5)
            {
                return Json(new { success = false, message = "Correction justification must be at least 5 characters long." });
            }

            int adminUserId = GetCurrentUserId();
            var result = await _dailyMilkService.CorrectDeliveryAsync(model, adminUserId, cancellationToken);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Message,
                deliveryId = result.DeliveryId
            });
        }

        /// <summary>
        /// Grants an explicit Administrator authorization permitting an unscheduled delivery on an off-schedule date.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AuthorizeUnscheduled([FromBody] CreateUnscheduledAuthorizationViewModel model, CancellationToken cancellationToken)
        {
            if (model == null)
            {
                return Json(new { success = false, message = "Invalid authorization payload." });
            }

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            {
                return Json(new { success = false, message = "Justification must be at least 5 characters long." });
            }

            int adminUserId = GetCurrentUserId();
            var result = await _dailyMilkService.AuthorizeUnscheduledDeliveryAsync(model, adminUserId, cancellationToken);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Message
            });
        }

        /// <summary>
        /// Revokes an unused unscheduled delivery authorization prior to consumption.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeUnscheduled([FromBody] RevokeUnscheduledRequest model, CancellationToken cancellationToken)
        {
            if (model == null || model.AuthorizationId <= 0)
            {
                return Json(new { success = false, message = "Invalid authorization identifier." });
            }

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            {
                return Json(new { success = false, message = "Revocation reason must be at least 5 characters long." });
            }

            int adminUserId = GetCurrentUserId();
            var result = await _dailyMilkService.RevokeUnscheduledAuthorizationAsync(model.AuthorizationId, model.Reason, adminUserId, cancellationToken);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Message
            });
        }

        /// <summary>
        /// Retrieves the immutable audit ledger entries for a delivery.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAuditHistory(long deliveryId, CancellationToken cancellationToken)
        {
            var history = await _dailyMilkService.GetCorrectionHistoryAsync(deliveryId, cancellationToken);
            return Json(new { success = true, data = history });
        }

        #endregion

        #region Lookups

        private async Task<List<DailyMilkCategoryHeaderModel>> GetActiveCategoriesAsync(CancellationToken cancellationToken)
        {
            var list = new List<DailyMilkCategoryHeaderModel>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT CategoryId, CategoryCode, CategoryName, ProductType, UnitOfMeasure, PricingBasis, DefaultPrice
                FROM [dbo].[MilkCategories]
                WHERE IsActive = 1
                ORDER BY DisplayOrder, CategoryName;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new DailyMilkCategoryHeaderModel
                {
                    CategoryId = reader.GetInt32(0),
                    CategoryCode = reader.GetString(1),
                    CategoryName = reader.GetString(2),
                    ProductType = reader.GetString(3),
                    UnitOfMeasure = reader.GetString(4),
                    PricingBasis = reader.GetString(5),
                    DefaultPrice = reader.GetDecimal(6)
                });
            }
            return list;
        }

        private async Task<List<DeliveryBoyLookupItem>> GetDeliveryBoysLookupAsync(CancellationToken cancellationToken)
        {
            var list = new List<DeliveryBoyLookupItem>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            const string sql = @"
                SELECT db.DeliveryBoyId, u.FullName, db.VehicleNumber, db.IsActive
                FROM [dbo].[DeliveryBoys] db
                JOIN [dbo].[Users] u ON db.UserId = u.UserId
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
                    Phone = null,
                    VehicleNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsActive = reader.GetBoolean(3)
                });
            }
            return list;
        }

        private async Task EnrichExistingDeliveryItemsAsync(DailyMilkRosterViewModel roster, CancellationToken cancellationToken)
        {
            if (roster == null || roster.Customers == null || roster.Customers.Count == 0) return;

            var deliveryIds = roster.Customers.Where(c => c.DeliveryId.HasValue).Select(c => c.DeliveryId!.Value).ToList();
            if (deliveryIds.Count == 0) return;

            var dict = new Dictionary<long, List<DailyMilkCategoryItemViewModel>>();
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            string inClause = string.Join(",", deliveryIds);
            string sql = $@"
                SELECT i.DeliveryId, i.CategoryId, c.CategoryName, i.ProductTypeSnapshot, i.UnitOfMeasureSnapshot,
                       i.PackagingQuantity, i.UnitPriceSnapshot, i.LitreEquivalentSnapshot, i.BillingUnitEquivalentSnapshot
                FROM [dbo].[DailyMilkDeliveryItems] i
                JOIN [dbo].[MilkCategories] c ON i.CategoryId = c.CategoryId
                WHERE i.DeliveryId IN ({inClause})
                ORDER BY c.DisplayOrder ASC, i.CategoryId ASC;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long delId = reader.GetInt64(0);
                if (!dict.TryGetValue(delId, out var list))
                {
                    list = new List<DailyMilkCategoryItemViewModel>();
                    dict[delId] = list;
                }
                list.Add(new DailyMilkCategoryItemViewModel
                {
                    CategoryId = reader.GetInt32(1),
                    CategoryName = reader.GetString(2),
                    ProductType = reader.GetString(3),
                    UnitOfMeasure = reader.GetString(4),
                    DefaultQuantity = reader.GetDecimal(5),
                    DeliveredQuantity = reader.GetDecimal(5),
                    UnitPrice = reader.GetDecimal(6),
                    LitreEquivalent = reader.GetDecimal(7),
                    BillingUnitEquivalent = reader.GetDecimal(8)
                });
            }

            foreach (var cust in roster.Customers)
            {
                if (cust.DeliveryId.HasValue && dict.TryGetValue(cust.DeliveryId.Value, out var items))
                {
                    cust.Categories = items;
                    cust.IsExistingEntry = true;
                }
            }
        }

        private async Task<bool> EnrichSubscribedCategoriesAsync(DailyMilkRosterViewModel roster, DateOnly targetDate, string targetShift, CancellationToken cancellationToken)
        {
            if (roster == null || roster.Customers == null || roster.Customers.Count == 0) return true;

            var unpopulatedCustomerIds = roster.Customers
                .Where(c => c.Categories == null || c.Categories.Count == 0)
                .Select(c => c.CustomerId)
                .Distinct()
                .ToList();

            if (unpopulatedCustomerIds.Count == 0) return true;

            try
            {
                var dict = new Dictionary<int, List<DailyMilkCategoryItemViewModel>>();
                await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

                string inClause = string.Join(",", unpopulatedCustomerIds);
                string sql = $@"
                    SELECT s.CustomerId, s.CategoryId, m.CategoryName, m.ProductType, m.UnitOfMeasure,
                           s.PackagingQuantity,
                           ISNULL(cp.CustomPrice, m.DefaultPrice) AS EffectivePricePerUnit,
                           m.LitreEquivalent, m.BillingUnitEquivalent,
                           CASE WHEN cp.CustomPrice IS NOT NULL THEN 1 ELSE 0 END AS IsCustomPrice
                    FROM [dbo].[CustomerSubscriptions] s
                    INNER JOIN [dbo].[MilkCategories] m ON s.CategoryId = m.CategoryId
                    LEFT JOIN [dbo].[CustomerCustomPricing] cp ON s.CustomerId = cp.CustomerId
                        AND s.CategoryId = cp.CategoryId
                        AND cp.IsActive = 1
                        AND cp.EffectiveFrom <= @TargetDate
                        AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @TargetDate)
                    WHERE s.CustomerId IN ({inClause})
                      AND s.IsActive = 1
                      AND s.EffectiveFrom <= @TargetDate
                      AND (s.EffectiveTo IS NULL OR s.EffectiveTo >= @TargetDate)
                      AND (@Shift = 'Both' OR s.Shift = 'Both' OR s.Shift = @Shift)
                      AND m.IsActive = 1
                    ORDER BY s.CustomerId ASC, m.DisplayOrder ASC, s.CategoryId ASC;";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@TargetDate", SqlDbType.Date).Value = targetDate.ToDateTime(TimeOnly.MinValue);
                cmd.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = targetShift;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    int custId = reader.GetInt32(0);
                    if (!dict.TryGetValue(custId, out var list))
                    {
                        list = new List<DailyMilkCategoryItemViewModel>();
                        dict[custId] = list;
                    }
                    list.Add(new DailyMilkCategoryItemViewModel
                    {
                        CategoryId = reader.GetInt32(1),
                        CategoryName = reader.GetString(2),
                        ProductType = reader.GetString(3),
                        UnitOfMeasure = reader.GetString(4),
                        DefaultQuantity = reader.GetDecimal(5),
                        DeliveredQuantity = reader.GetDecimal(5),
                        UnitPrice = reader.GetDecimal(6),
                        LitreEquivalent = reader.GetDecimal(7),
                        BillingUnitEquivalent = reader.GetDecimal(8),
                        IsCustomPrice = reader.GetInt32(9) == 1
                    });
                }

                foreach (var cust in roster.Customers)
                {
                    if ((cust.Categories == null || cust.Categories.Count == 0) && dict.TryGetValue(cust.CustomerId, out var items))
                    {
                        cust.Categories = items;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load customer subscription records for Date: {Date}, Shift: {Shift}", targetDate, targetShift);
                return false;
            }
        }

        #endregion
    }

    public class RevokeUnscheduledRequest
    {
        public long AuthorizationId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
