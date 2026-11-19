using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Compensation;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Administrator controller managing Monthly Compensation and Payroll Statements,
    /// bulk draft calculations, immutable ledger adjustments, statement locks (Finalize),
    /// and verified payout recording.
    /// Strictly restricted to Admin users.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class CompensationController : Controller
    {
        private readonly ICompensationService _compensationService;
        private readonly IDeliveryBoyService _deliveryBoyService;
        private readonly ILogger<CompensationController> _logger;

        public CompensationController(
            ICompensationService compensationService,
            IDeliveryBoyService deliveryBoyService,
            ILogger<CompensationController> logger)
        {
            _compensationService = compensationService ?? throw new ArgumentNullException(nameof(compensationService));
            _deliveryBoyService = deliveryBoyService ?? throw new ArgumentNullException(nameof(deliveryBoyService));
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

        #region 1. Monthly Payroll & Compensation Index

        [HttpGet]
        public async Task<IActionResult> Index(
            byte? month,
            short? year,
            string? status,
            string? compensationType,
            int? deliveryBoyId,
            int page = 1,
            int pageSize = 10,
            CancellationToken ct = default)
        {
            try
            {
                byte targetMonth = month.HasValue && month.Value >= 1 && month.Value <= 12
                    ? month.Value
                    : (byte)DateTime.Today.Month;

                short targetYear = year.HasValue && year.Value >= 2000 && year.Value <= 2100
                    ? year.Value
                    : (short)DateTime.Today.Year;

                var criteria = new CompensationFilterCriteria
                {
                    Month = targetMonth,
                    Year = targetYear,
                    Status = string.IsNullOrWhiteSpace(status) ? "All" : status.Trim(),
                    CompensationType = string.IsNullOrWhiteSpace(compensationType) ? "All" : compensationType.Trim(),
                    DeliveryBoyId = deliveryBoyId
                };

                // Query payroll statements from service
                string? statusFilter = criteria.Status == "All" ? null : criteria.Status;
                string? compTypeFilter = criteria.CompensationType == "All" ? null : criteria.CompensationType;

                var list = await _compensationService.GetCompensationsAsync(targetMonth, targetYear, statusFilter, compTypeFilter, ct);

                // Filter by DeliveryBoy if specified
                if (deliveryBoyId.HasValue && deliveryBoyId.Value > 0)
                {
                    list = list.Where(c => c.DeliveryBoyId == deliveryBoyId.Value).ToList();
                }

                // Summary KPIs
                int totalEmployees = list.Count;
                int draftCount = list.Count(c => c.Status == "Draft");
                int finalizedCount = list.Count(c => c.Status == "Finalized");
                int paidCount = list.Count(c => c.Status == "Paid");
                decimal totalGross = list.Sum(c => c.GrossEarnings);
                decimal totalAdjustments = list.Sum(c => c.Adjustments);
                decimal totalNet = list.Sum(c => c.NetPayableAmount);

                // Pagination
                int safePage = page < 1 ? 1 : page;
                int safePageSize = pageSize < 1 ? 10 : pageSize;
                var pagedList = list
                    .Skip((safePage - 1) * safePageSize)
                    .Take(safePageSize)
                    .ToList();

                var activeDeliveryBoys = await _deliveryBoyService.GetActiveLookupAsync(ct);

                var viewModel = new CompensationIndexViewModel
                {
                    Criteria = criteria,
                    Compensations = pagedList,
                    AvailableDeliveryBoys = activeDeliveryBoys,
                    TotalEmployees = totalEmployees,
                    DraftCount = draftCount,
                    FinalizedCount = finalizedCount,
                    PaidCount = paidCount,
                    TotalGrossPayable = totalGross,
                    TotalAdjustments = totalAdjustments,
                    TotalNetPayable = totalNet,
                    TotalCount = totalEmployees,
                    CurrentPage = safePage,
                    PageSize = safePageSize
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading monthly compensation index");
                TempData["ErrorMessage"] = "Unable to load payroll statements. Please try again.";
                return View(new CompensationIndexViewModel());
            }
        }

        #endregion

        #region 2. Statement Details

        [HttpGet]
        public async Task<IActionResult> Details(long id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Invalid compensation statement ID.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var details = await _compensationService.GetCompensationDetailsAsync(id, ct);
                if (details == null)
                {
                    TempData["ErrorMessage"] = $"Compensation statement #{id} was not found.";
                    return RedirectToAction(nameof(Index));
                }

                return View(details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading compensation statement details for ID {CompensationId}", id);
                TempData["ErrorMessage"] = "Unable to load statement details. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion

        #region 3. Draft Calculations (Single & Bulk)

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CalculateDraft(int deliveryBoyId, byte month, short year, CancellationToken ct = default)
        {
            try
            {
                int adminUserId = GetCurrentUserId();
                long compId = await _compensationService.CalculateOrUpdateDraftAsync(deliveryBoyId, month, year, adminUserId, ct);

                TempData["SuccessMessage"] = $"Draft compensation statement calculated successfully (ID #{compId}).";
                return RedirectToAction(nameof(Details), new { id = compId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during CalculateDraft");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error calculating draft for DeliveryBoyId {DeliveryBoyId}, {Month}/{Year}", deliveryBoyId, month, year);
                TempData["ErrorMessage"] = ex.Number == 50046
                    ? "Cannot recalculate a compensation record that is already Finalized or Paid."
                    : "Database error occurred while calculating draft compensation.";
                return RedirectToAction(nameof(Index), new { month, year, deliveryBoyId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calculating draft compensation");
                TempData["ErrorMessage"] = "An unexpected error occurred while calculating draft compensation.";
                return RedirectToAction(nameof(Index), new { month, year, deliveryBoyId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkCalculate(byte month, short year, CancellationToken ct = default)
        {
            try
            {
                int adminUserId = GetCurrentUserId();
                int totalCalculated = await _compensationService.BulkCalculateDraftsAsync(month, year, adminUserId, ct);

                TempData["SuccessMessage"] = $"Bulk draft calculation completed. Generated/updated {totalCalculated} statement(s) for {month:D2}/{year}.";
                return RedirectToAction(nameof(Index), new { month, year });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during BulkCalculate");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running bulk draft compensation calculation for {Month}/{Year}", month, year);
                TempData["ErrorMessage"] = "An error occurred during bulk draft calculation. Please try again.";
                return RedirectToAction(nameof(Index), new { month, year });
            }
        }

        #endregion

        #region 4. Compensation Adjustments (Ledger Additions)

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyAdjustment(ApplyCompensationAdjustmentViewModel model, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            {
                TempData["ErrorMessage"] = "Adjustment justification reason is required and must be at least 5 characters long.";
                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }

            if (model.AdjustmentAmount == 0.00m)
            {
                TempData["ErrorMessage"] = "Adjustment amount cannot be zero.";
                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _compensationService.ApplyAdjustmentAsync(model, adminUserId, ct);

                if (success)
                {
                    string sign = model.AdjustmentAmount > 0 ? "+" : "";
                    TempData["SuccessMessage"] = $"Adjustment of {sign}₹{model.AdjustmentAmount:N2} ({model.AdjustmentType}) successfully applied to ledger.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Could not apply adjustment to statement.";
                }

                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during ApplyAdjustment");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error applying adjustment to CompensationId {CompensationId}", model.CompensationId);
                switch (ex.Number)
                {
                    case 50047:
                        TempData["ErrorMessage"] = "Adjustments are strictly restricted to Draft compensation statements. Finalized and Paid statements are immutable.";
                        break;
                    case 50048:
                        TempData["ErrorMessage"] = "Adjustment rejected: Net payable amount cannot become negative. Total deductions exceed gross earnings.";
                        break;
                    case 50049:
                        TempData["ErrorMessage"] = "Adjustment justification reason must be at least 5 characters long.";
                        break;
                    default:
                        TempData["ErrorMessage"] = "A database error occurred while recording the adjustment. Please verify and retry.";
                        break;
                }

                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error applying adjustment to CompensationId {CompensationId}", model.CompensationId);
                TempData["ErrorMessage"] = "An unexpected error occurred while applying the adjustment.";
                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
        }

        #endregion

        #region 5. Finalize Compensation

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finalize(long compensationId, CancellationToken ct = default)
        {
            if (compensationId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid compensation statement ID.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _compensationService.FinalizeCompensationAsync(compensationId, adminUserId, ct);

                if (success)
                {
                    TempData["SuccessMessage"] = $"Compensation statement #{compensationId} has been successfully Finalized and locked.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to finalize compensation statement.";
                }

                return RedirectToAction(nameof(Details), new { id = compensationId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during Finalize");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error finalizing CompensationId {CompensationId}", compensationId);
                TempData["ErrorMessage"] = ex.Number == 50050
                    ? "Only Draft compensation records can be finalized."
                    : "Database error occurred during statement finalization.";
                return RedirectToAction(nameof(Details), new { id = compensationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error finalizing CompensationId {CompensationId}", compensationId);
                TempData["ErrorMessage"] = "An unexpected error occurred while finalizing the statement.";
                return RedirectToAction(nameof(Details), new { id = compensationId });
            }
        }

        #endregion

        #region 6. Record Payout

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordPayout(RecordCompensationPayoutViewModel model, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model.PaymentReference))
            {
                TempData["ErrorMessage"] = "Payment reference is required to record payout disbursement.";
                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }

            try
            {
                int adminUserId = GetCurrentUserId();
                bool success = await _compensationService.RecordPayoutAsync(model, adminUserId, ct);

                if (success)
                {
                    TempData["SuccessMessage"] = $"Payout recorded successfully. Reference: '{model.PaymentReference}'. Status updated to Paid.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to record payout disbursement.";
                }

                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized attempt during RecordPayout");
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("AdminLogin", "Auth");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error recording payout for CompensationId {CompensationId}", model.CompensationId);
                switch (ex.Number)
                {
                    case 50051:
                        TempData["ErrorMessage"] = "Payout can only be recorded on Finalized compensation records.";
                        break;
                    case 50052:
                        TempData["ErrorMessage"] = "This compensation statement has already been marked as Paid.";
                        break;
                    default:
                        TempData["ErrorMessage"] = "Database error occurred while recording payout.";
                        break;
                }

                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error recording payout for CompensationId {CompensationId}", model.CompensationId);
                TempData["ErrorMessage"] = "An unexpected error occurred while recording payout.";
                return RedirectToAction(nameof(Details), new { id = model.CompensationId });
            }
        }

        #endregion
    }
}
