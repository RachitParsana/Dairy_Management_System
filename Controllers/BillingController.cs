using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Billing;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.Route;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    #region Supporting View Models for Billing Views

    public class BillingCustomerOption
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string DisplayText => $"{FullName} ({CustomerCode}) - {Phone}";
    }

    public class BillingRouteOption
    {
        public int RouteId { get; set; }
        public string RouteCode { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
    }

    public class BillingIndexViewModel
    {
        public MonthlyBillFilterCriteria Criteria { get; set; } = new();
        public IReadOnlyList<MonthlyBill> Bills { get; set; } = new List<MonthlyBill>();
        public int TotalCount { get; set; }
        public BillingSummaryReportModel Summary { get; set; } = new();
        public IReadOnlyList<BillingRouteOption> Routes { get; set; } = new List<BillingRouteOption>();
        public IReadOnlyList<BillingCustomerOption> Customers { get; set; } = new List<BillingCustomerOption>();
    }

    public class BulkBillingPageViewModel
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public int? RouteId { get; set; }
        public IReadOnlyList<BillingRouteOption> Routes { get; set; } = new List<BillingRouteOption>();
        public IReadOnlyList<CustomerBillingEligibilityModel> EligibleCustomers { get; set; } = new List<CustomerBillingEligibilityModel>();
        public BulkBillingResultModel? LastRunResult { get; set; }
    }

    public class PaymentsPageViewModel
    {
        public PaymentFilterCriteria Criteria { get; set; } = new();
        public IReadOnlyList<Payment> Payments { get; set; } = new List<Payment>();
        public int TotalCount { get; set; }
        public IReadOnlyList<BillingCustomerOption> Customers { get; set; } = new List<BillingCustomerOption>();
        public int? SelectedCustomerId { get; set; }
        public CustomerBalanceSummaryModel? SelectedCustomerBalance { get; set; }
        public IReadOnlyList<CustomerOutstandingBillItemViewModel> SelectedCustomerOutstandingBills { get; set; } = new List<CustomerOutstandingBillItemViewModel>();
    }

    public class BillingReportsPageViewModel
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public BillingSummaryReportModel Report { get; set; } = new();
    }

    #endregion

    /// <summary>
    /// Administrator controller orchestrating the complete Day 5 Monthly Billing & Payments Management interface.
    /// Strictly authorized for the Admin role.
    /// Strictly append-only financial model, manual payment allocation to ONE selected invoice,
    /// LIFO multi-payment bill revisions, and two-tiered payment void idempotency.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class BillingController : Controller
    {
        private readonly IBillingService _billingService;
        private readonly ICustomerService _customerService;
        private readonly IRouteService _routeService;
        private readonly ILogger<BillingController> _logger;

        public BillingController(
            IBillingService billingService,
            ICustomerService customerService,
            IRouteService routeService,
            ILogger<BillingController> logger)
        {
            _billingService = billingService ?? throw new ArgumentNullException(nameof(billingService));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Current Admin Identity & Lookups

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idClaim, out var id) && id > 0 ? id : 1; // Default to System Administrator (UserId = 1)
        }

        private async Task<IReadOnlyList<BillingRouteOption>> GetRoutesLookupAsync(CancellationToken cancellationToken)
        {
            try
            {
                var routeList = await _routeService.GetActiveRoutesLookupAsync(cancellationToken);

                return routeList
                    .Select(r => new BillingRouteOption
                    {
                        RouteId = r.RouteId,
                        RouteCode = r.RouteCode,
                        RouteName = r.RouteName
                    })
                    .OrderBy(r => r.RouteName)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load active routes lookup from route service.");
                TempData["ErrorMessage"] = "Unable to load the active routes list. Please refresh the page or contact the system administrator.";
                return Array.Empty<BillingRouteOption>();
            }
        }

        private async Task<IReadOnlyList<BillingCustomerOption>> GetActiveCustomersLookupAsync(CancellationToken cancellationToken)
        {
            try
            {
                var customerList = await _customerService.GetCustomerListAsync(
                    new CustomerFilterCriteria { Status = "Active", PageSize = 1000 },
                    cancellationToken);

                return customerList.Select(c => new BillingCustomerOption
                {
                    CustomerId = c.CustomerId,
                    CustomerCode = c.CustomerCode,
                    FullName = c.FullName,
                    Phone = c.Phone
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load active customers lookup from customer service.");
                TempData["ErrorMessage"] = "Unable to load customer list. Please refresh the page or contact the system administrator.";
                return Array.Empty<BillingCustomerOption>();
            }
        }

        #endregion

        #region 1. Admin Billing Dashboard (/Billing)

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] MonthlyBillFilterCriteria? criteria, CancellationToken cancellationToken)
        {
            criteria ??= new MonthlyBillFilterCriteria();

            // Default to current month/year if not provided
            var currentMonth = DateTime.Today.Month;
            var currentYear = DateTime.Today.Year;

            var targetMonth = criteria.BillingMonth ?? (byte)currentMonth;
            var targetYear = criteria.BillingYear ?? (short)currentYear;

            // Fetch live bills feed
            var (bills, totalCount) = await _billingService.GetBillsContinuousAsync(criteria, cancellationToken);

            // Fetch aggregate summary report for metric cards
            BillingSummaryReportModel summary;
            try
            {
                summary = await _billingService.GetMonthlyBillingSummaryReportAsync(targetMonth, targetYear, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load billing summary report for {Month}/{Year}. Defaulting to empty summary.", targetMonth, targetYear);
                summary = new BillingSummaryReportModel
                {
                    BillingMonth = targetMonth,
                    BillingYear = targetYear
                };
            }

            var routes = await GetRoutesLookupAsync(cancellationToken);
            var customers = await GetActiveCustomersLookupAsync(cancellationToken);

            var viewModel = new BillingIndexViewModel
            {
                Criteria = criteria,
                Bills = bills,
                TotalCount = totalCount,
                Summary = summary,
                Routes = routes,
                Customers = customers
            };

            ViewData["Title"] = "Monthly Billing Management";
            ViewData["ActiveMenu"] = "Billing";
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateBill(int customerId, int month, int year, CancellationToken cancellationToken)
        {
            if (customerId <= 0 || month < 1 || month > 12 || year < 2000 || year > 2100)
            {
                TempData["ErrorMessage"] = "Valid customer, billing month (1-12) and year are required.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                long billId = await _billingService.GenerateCustomerBillAsync(customerId, month, year, adminUserId, cancellationToken);
                TempData["SuccessMessage"] = $"Draft invoice #{billId} successfully generated from completed deliveries.";
                return RedirectToAction(nameof(Details), new { id = billId });
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error generating bill for Customer {CustomerId}, Month: {Month}/{Year}", customerId, month, year);
                TempData["ErrorMessage"] = "A database error occurred while generating the invoice. Please try again or contact support.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating bill for Customer {CustomerId}, Month: {Month}/{Year}", customerId, month, year);
                TempData["ErrorMessage"] = "An unexpected error occurred while generating the invoice. Please try again or contact support.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PublishBill(long billId, int? graceDays, string? returnUrl, CancellationToken cancellationToken)
        {
            if (billId <= 0)
            {
                TempData["ErrorMessage"] = "Valid Bill ID is required.";
                return RedirectToAction(nameof(Index));
            }

            if (graceDays.HasValue && (graceDays.Value <= 0 || graceDays.Value > 90))
            {
                TempData["ErrorMessage"] = "Grace period must be between 1 and 90 days, or left blank for no due date.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Details), new { id = billId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool published = await _billingService.PublishBillAsync(billId, graceDays, adminUserId, cancellationToken);
                if (published)
                {
                    TempData["SuccessMessage"] = graceDays.HasValue
                        ? $"Invoice #{billId} published successfully with snapshotted payment due date (+{graceDays.Value} days grace period)."
                        : $"Invoice #{billId} published successfully without due date (PaymentDueDate = NULL).";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Invoice #{billId} could not be published.";
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error publishing bill #{BillId}", billId);
                TempData["ErrorMessage"] = "A database error occurred while publishing the invoice. Please try again or contact support.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing bill #{BillId}", billId);
                TempData["ErrorMessage"] = "An unexpected error occurred while publishing the invoice. Please try again or contact support.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Details), new { id = billId });
        }

        #endregion

        #region 2. Bulk Billing Page (/Billing/BulkBilling)

        [HttpGet]
        public async Task<IActionResult> BulkBilling(int? month, int? year, int? routeId, CancellationToken cancellationToken)
        {
            var targetMonth = month ?? DateTime.Today.Month;
            var targetYear = year ?? DateTime.Today.Year;

            var routes = await GetRoutesLookupAsync(cancellationToken);
            var eligibleCustomers = await _billingService.GetEligibleCustomersForBillingAsync(targetMonth, targetYear, routeId, cancellationToken);

            var viewModel = new BulkBillingPageViewModel
            {
                Month = targetMonth,
                Year = targetYear,
                RouteId = routeId,
                Routes = routes,
                EligibleCustomers = eligibleCustomers
            };

            ViewData["Title"] = "Bulk Monthly Billing";
            ViewData["ActiveMenu"] = "Billing";
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateBulk(int month, int year, int? routeId, CancellationToken cancellationToken)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
            {
                TempData["ErrorMessage"] = "Valid billing month (1-12) and year are required.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                var result = await _billingService.GenerateBulkMonthlyBillsAsync(month, year, routeId, adminUserId, cancellationToken);

                TempData["SuccessMessage"] = $"Bulk generation complete! Succeeded: {result.TotalSucceeded}, Skipped (Already Billed): {result.TotalSkipped}, Failed: {result.TotalFailed} out of {result.TotalEligible} eligible accounts.";

                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk monthly billing generation failed for {Month}/{Year}, Route: {RouteId}", month, year, routeId);
                TempData["ErrorMessage"] = "A server error occurred during bulk invoice generation. Please check the logs or contact support.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkPublish(int month, int year, int? routeId, int? graceDays, CancellationToken cancellationToken)
        {
            if (month < 1 || month > 12 || year < 2000 || year > 2100)
            {
                TempData["ErrorMessage"] = "Valid billing month (1-12) and year are required.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }

            if (graceDays.HasValue && (graceDays.Value <= 0 || graceDays.Value > 90))
            {
                TempData["ErrorMessage"] = "Grace period must be between 1 and 90 days, or left blank for no due date.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                int publishedCount = await _billingService.BulkPublishBillsAsync(month, year, routeId, graceDays, adminUserId, cancellationToken);

                TempData["SuccessMessage"] = graceDays.HasValue
                    ? $"Bulk publish operation completed. Total draft invoices published with +{graceDays.Value} days due date: {publishedCount}."
                    : $"Bulk publish operation completed. Total draft invoices published without due date (PaymentDueDate = NULL): {publishedCount}.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error bulk publishing for {Month}/{Year}, Route: {RouteId}", month, year, routeId);
                TempData["ErrorMessage"] = "A database error occurred during bulk invoice publishing. Please try again or contact support.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk publishing failed for {Month}/{Year}, Route: {RouteId}", month, year, routeId);
                TempData["ErrorMessage"] = "An error occurred during bulk invoice publishing. Please check the logs or contact support.";
                return RedirectToAction(nameof(BulkBilling), new { month, year, routeId });
            }
        }

        #endregion

        #region 3. Bill Details & Revision History (/Billing/Details/{id})

        [HttpGet]
        public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Valid Bill ID is required.";
                return RedirectToAction(nameof(Index));
            }

            var bill = await _billingService.GetBillDetailsAsync(id, cancellationToken);
            if (bill == null)
            {
                TempData["ErrorMessage"] = $"Invoice #{id} was not found.";
                return RedirectToAction(nameof(Index));
            }

            // Load customer's real-time overall balance summary for context
            CustomerBalanceSummaryModel? balanceSummary = null;
            try
            {
                balanceSummary = await _billingService.GetCustomerBalanceSummaryAsync(bill.CustomerId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load customer balance summary for CustomerId: {CustomerId}", bill.CustomerId);
            }

            ViewBag.BalanceSummary = balanceSummary;
            ViewData["Title"] = $"Invoice Details - {bill.BillNumber}";
            ViewData["ActiveMenu"] = "Billing";
            return View(bill);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviseBill(ReviseBillViewModel model, CancellationToken cancellationToken)
        {
            if (model == null || model.BillId <= 0)
            {
                TempData["ErrorMessage"] = "Valid Bill ID is required.";
                return RedirectToAction(nameof(Index));
            }

            if (model.Adjustments < 0)
            {
                TempData["ErrorMessage"] = "Adjustments cannot be negative.";
                return RedirectToAction(nameof(Details), new { id = model.BillId });
            }

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            {
                TempData["ErrorMessage"] = "Revision justification reason is mandatory and must be at least 5 characters.";
                return RedirectToAction(nameof(Details), new { id = model.BillId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool revised = await _billingService.ReviseBillAsync(model, adminUserId, cancellationToken);
                if (revised)
                {
                    TempData["SuccessMessage"] = $"Invoice #{model.BillId} revised successfully! Adjustments updated to ₹{model.Adjustments:N2} with LIFO allocation audit.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Invoice #{model.BillId} could not be revised. Concurrency version mismatch.";
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error revising BillId: {BillId}", model.BillId);
                TempData["ErrorMessage"] = "A database error occurred while revising the invoice. Please try again or contact support.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising BillId: {BillId}", model.BillId);
                TempData["ErrorMessage"] = "An unexpected error occurred while revising the invoice. Please try again or contact support.";
            }

            return RedirectToAction(nameof(Details), new { id = model.BillId });
        }

        #endregion

        #region 4. Payments & Advance Credit Ledger (/Billing/Payments)

        [HttpGet]
        public async Task<IActionResult> Payments([FromQuery] PaymentFilterCriteria? criteria, int? selectedCustomerId, CancellationToken cancellationToken)
        {
            criteria ??= new PaymentFilterCriteria();
            if (selectedCustomerId.HasValue && selectedCustomerId.Value > 0)
            {
                criteria.CustomerId = selectedCustomerId.Value;
            }

            var (payments, totalCount) = await _billingService.GetPaymentsHistoryAsync(criteria, cancellationToken);
            var customers = await GetActiveCustomersLookupAsync(cancellationToken);

            CustomerBalanceSummaryModel? customerBalance = null;
            IReadOnlyList<CustomerOutstandingBillItemViewModel> outstandingBills = new List<CustomerOutstandingBillItemViewModel>();

            if (criteria.CustomerId.HasValue && criteria.CustomerId.Value > 0)
            {
                try
                {
                    customerBalance = await _billingService.GetCustomerBalanceSummaryAsync(criteria.CustomerId.Value, cancellationToken);
                    outstandingBills = await _billingService.GetCustomerOutstandingBillsAsync(criteria.CustomerId.Value, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load balance/outstanding bills for CustomerId: {CustomerId}", criteria.CustomerId.Value);
                }
            }

            var viewModel = new PaymentsPageViewModel
            {
                Criteria = criteria,
                Payments = payments,
                TotalCount = totalCount,
                Customers = customers,
                SelectedCustomerId = criteria.CustomerId,
                SelectedCustomerBalance = customerBalance,
                SelectedCustomerOutstandingBills = outstandingBills
            };

            ViewData["Title"] = "Payment Receipts & Advance Credit";
            ViewData["ActiveMenu"] = "Billing";
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordPayment(RecordSinglePaymentViewModel model, CancellationToken cancellationToken)
        {
            if (model == null)
            {
                TempData["ErrorMessage"] = "Invalid payment data.";
                return RedirectToAction(nameof(Payments));
            }

            if (model.CustomerId <= 0)
            {
                TempData["ErrorMessage"] = "Customer selection is required.";
                return RedirectToAction(nameof(Payments));
            }

            if (model.TargetBillId <= 0)
            {
                TempData["ErrorMessage"] = "Target invoice selection is required. Each payment must explicitly target exactly ONE invoice.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }

            if (model.Amount <= 0)
            {
                TempData["ErrorMessage"] = "Payment amount must be greater than zero.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }

            if (model.PaymentMode != "Online" && model.PaymentMode != "Offline")
            {
                TempData["ErrorMessage"] = "Payment mode must be strictly 'Online' or 'Offline'.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                long paymentId = await _billingService.RecordAndAllocateSinglePaymentAsync(model, adminUserId, cancellationToken);
                TempData["SuccessMessage"] = $"Payment receipt #{paymentId} of ₹{model.Amount:N2} recorded and allocated to Bill #{model.TargetBillId}. Any excess amount is safely retained as unallocated advance credit.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error recording payment for CustomerId: {CustomerId}", model.CustomerId);
                TempData["ErrorMessage"] = "A database error occurred while recording the payment. Please try again or contact support.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording payment for CustomerId: {CustomerId}", model.CustomerId);
                TempData["ErrorMessage"] = "An unexpected error occurred while recording the payment. Please try again or contact support.";
                return RedirectToAction(nameof(Payments), new { selectedCustomerId = model.CustomerId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AllocateAdvanceCredit(AllocateAdvanceCreditViewModel model, CancellationToken cancellationToken)
        {
            if (model == null || model.PaymentId <= 0 || model.TargetBillId <= 0 || model.Amount <= 0)
            {
                TempData["ErrorMessage"] = "Valid payment receipt, target invoice, and positive credit amount are required.";
                return RedirectToAction(nameof(Payments));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool allocated = await _billingService.AllocateAdvanceCreditSingleAsync(model, adminUserId, cancellationToken);
                if (allocated)
                {
                    TempData["SuccessMessage"] = $"₹{model.Amount:N2} advance credit successfully allocated from Payment #{model.PaymentId} to Invoice #{model.TargetBillId}.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Advance credit allocation failed.";
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error allocating advance credit from Payment #{PaymentId}", model.PaymentId);
                TempData["ErrorMessage"] = "A database error occurred while allocating advance credit. Please try again or contact support.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error allocating advance credit from Payment #{PaymentId}", model.PaymentId);
                TempData["ErrorMessage"] = "An unexpected error occurred while allocating advance credit. Please try again or contact support.";
            }

            return RedirectToAction(nameof(Payments));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidPayment(VoidPaymentViewModel model, CancellationToken cancellationToken)
        {
            if (model == null || model.PaymentId <= 0)
            {
                TempData["ErrorMessage"] = "Valid Payment ID is required.";
                return RedirectToAction(nameof(Payments));
            }

            if (string.IsNullOrWhiteSpace(model.VoidReason) || model.VoidReason.Trim().Length < 10)
            {
                TempData["ErrorMessage"] = "Void justification reason is mandatory and must be at least 10 characters in length.";
                return RedirectToAction(nameof(Payments));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool voided = await _billingService.VoidPaymentAsync(model, adminUserId, cancellationToken);
                if (voided)
                {
                    TempData["SuccessMessage"] = $"Payment receipt #{model.PaymentId} has been successfully VOIDED. All applicable invoice allocations have been reversed and customer balances recalculated.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Payment receipt #{model.PaymentId} could not be voided.";
                }
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL error voiding Payment #{PaymentId}", model.PaymentId);
                TempData["ErrorMessage"] = "A database error occurred while voiding the payment. Please try again or contact support.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error voiding Payment #{PaymentId}", model.PaymentId);
                TempData["ErrorMessage"] = "An unexpected error occurred while voiding the payment. Please try again or contact support.";
            }

            return RedirectToAction(nameof(Payments));
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomerOutstandingBills(int customerId, CancellationToken cancellationToken)
        {
            if (customerId <= 0)
            {
                return Json(new { success = false, message = "Valid Customer ID is required." });
            }

            try
            {
                var bills = await _billingService.GetCustomerOutstandingBillsAsync(customerId, cancellationToken);
                var balance = await _billingService.GetCustomerBalanceSummaryAsync(customerId, cancellationToken);
                return Json(new
                {
                    success = true,
                    outstandingBills = bills,
                    balanceSummary = balance
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve outstanding bills for CustomerId: {CustomerId}", customerId);
                return Json(new { success = false, message = "Failed to load customer billing data." });
            }
        }

        #endregion

        #region 5. Billing Reports (/Billing/Reports)

        [HttpGet]
        public async Task<IActionResult> Reports(int? month, int? year, CancellationToken cancellationToken)
        {
            var targetMonth = month ?? DateTime.Today.Month;
            var targetYear = year ?? DateTime.Today.Year;

            BillingSummaryReportModel report;
            try
            {
                report = await _billingService.GetMonthlyBillingSummaryReportAsync(targetMonth, targetYear, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load billing summary report for {Month}/{Year}. Defaulting to empty report.", targetMonth, targetYear);
                report = new BillingSummaryReportModel
                {
                    BillingMonth = (byte)targetMonth,
                    BillingYear = (short)targetYear
                };
            }

            var viewModel = new BillingReportsPageViewModel
            {
                Month = targetMonth,
                Year = targetYear,
                Report = report
            };

            ViewData["Title"] = "Monthly Billing & Collections Reports";
            ViewData["ActiveMenu"] = "Billing";
            return View(viewModel);
        }

        #endregion
    }
}
