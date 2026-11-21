using System;
using System.Data;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.DeliveryBoy;
using Dairy_Management_System.Models.Settings;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Controller managing administrative enterprise configurations and delivery partner field preferences.
    /// Strictly role-partitioned: Enterprise settings are Admin-only; preferences and profile are DeliveryBoy-only.
    /// </summary>
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly ISettingsService _settingsService;
        private readonly IDeliveryBoyService _deliveryBoyService;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ISettingsService settingsService,
            IDeliveryBoyService deliveryBoyService,
            ISqlConnectionFactory connectionFactory,
            ILogger<SettingsController> logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _deliveryBoyService = deliveryBoyService ?? throw new ArgumentNullException(nameof(deliveryBoyService));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Claims Identity

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idClaim, out var id) ? id : 0;
        }

        private async Task<int?> ResolveDeliveryBoyIdByUserIdAsync(int userId, CancellationToken ct)
        {
            const string sql = @"
                SELECT db.[DeliveryBoyId]
                FROM [dbo].[DeliveryBoys] db
                WHERE db.[UserId] = @UserId AND db.[IsActive] = 1;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }

            return null;
        }

        #endregion

        #region Admin Enterprise Settings

        /// <summary>
        /// Admin Enterprise Settings console. Displays current operational settings with neutral defaults.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync(ct).ConfigureAwait(false);
                return View(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load system settings for Admin.");
                TempData["ErrorMessage"] = "Unable to load system settings at this time.";
                return View(new SystemSettingsViewModel());
            }
        }

        /// <summary>
        /// Saves updated enterprise settings. Strictly prohibits automatic invoice due dates.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(SystemSettingsViewModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            int adminUserId = GetCurrentUserId();
            if (adminUserId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid administrator session. Please log in again.";
                return RedirectToAction("AdminLogin", "Auth");
            }

            try
            {
                var success = await _settingsService.UpdateSettingsAsync(model, adminUserId, ct).ConfigureAwait(false);
                if (success)
                {
                    TempData["SuccessMessage"] = "Enterprise settings updated successfully. Changes are now active across the platform.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to update one or more settings.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update enterprise settings by Admin {AdminId}", adminUserId);
                TempData["ErrorMessage"] = "An error occurred while saving enterprise settings.";
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Delivery Boy Field Settings & Preferences

        /// <summary>
        /// Delivery partner preferences page (language, display, runsheet behavior).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "DeliveryBoy")]
        public IActionResult DeliveryBoySettings()
        {
            // Read saved preference from cookie or fallback to default
            var prefs = new DeliveryBoyPreferencesViewModel
            {
                PreferredLanguage = Request.Cookies["dms_db_lang"] ?? "en",
                DefaultShiftView = Request.Cookies["dms_db_shift"] ?? "Morning",
                EnableSoundAlerts = (Request.Cookies["dms_db_sound"] ?? "true") == "true",
                HighContrastRunsheet = (Request.Cookies["dms_db_contrast"] ?? "false") == "true",
                AutoAdvanceNextCustomer = (Request.Cookies["dms_db_advance"] ?? "true") == "true"
            };

            return View(prefs);
        }

        /// <summary>
        /// Saves delivery partner mobile field preferences into client cookies.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "DeliveryBoy")]
        [ValidateAntiForgeryToken]
        public IActionResult SavePreferences(DeliveryBoyPreferencesViewModel model)
        {
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Secure = Request.IsHttps
            };

            Response.Cookies.Append("dms_db_lang", model.PreferredLanguage ?? "en", cookieOptions);
            Response.Cookies.Append("dms_db_shift", model.DefaultShiftView ?? "Morning", cookieOptions);
            Response.Cookies.Append("dms_db_sound", model.EnableSoundAlerts ? "true" : "false", cookieOptions);
            Response.Cookies.Append("dms_db_contrast", model.HighContrastRunsheet ? "true" : "false", cookieOptions);
            Response.Cookies.Append("dms_db_advance", model.AutoAdvanceNextCustomer ? "true" : "false", cookieOptions);

            TempData["SuccessMessage"] = "Field preferences saved to device browser cookies.";
            return RedirectToAction(nameof(DeliveryBoySettings));
        }

        #endregion

        #region Delivery Boy Profile & Compensation Details

        /// <summary>
        /// Displays the authenticated Delivery Boy's profile, route assignment, vehicle info, and payout history.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> Profile(CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Delivery Boy profile accessed without valid UserId claim.");
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            try
            {
                int? deliveryBoyId = await ResolveDeliveryBoyIdByUserIdAsync(userId, ct).ConfigureAwait(false);
                if (!deliveryBoyId.HasValue)
                {
                    _logger.LogWarning("No active DeliveryBoy master record found for UserId: {UserId}", userId);
                    TempData["ErrorMessage"] = "No active delivery partner profile linked to your user account.";
                    return RedirectToAction("Privacy", "Home");
                }

                var profile = await _deliveryBoyService.GetDetailsAsync(deliveryBoyId.Value, ct).ConfigureAwait(false);
                if (profile == null)
                {
                    TempData["ErrorMessage"] = "Delivery partner profile record not found.";
                    return RedirectToAction("Privacy", "Home");
                }

                return View(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Delivery Boy profile for UserId: {UserId}", userId);
                TempData["ErrorMessage"] = "An error occurred while loading your profile.";
                return RedirectToAction("Privacy", "Home");
            }
        }

        #endregion
    }
}
