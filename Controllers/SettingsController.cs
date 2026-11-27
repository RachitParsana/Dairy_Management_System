using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Models.DeliveryBoy;
using Dairy_Management_System.Models.Settings;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Controller managing administrative enterprise configurations, Admin profile/password,
    /// and delivery partner field preferences, profile updates, and password management.
    /// </summary>
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly ISettingsService _settingsService;
        private readonly IDeliveryBoyService _deliveryBoyService;
        private readonly IUserAccountService? _userAccountService;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ISettingsService settingsService,
            IDeliveryBoyService deliveryBoyService,
            ISqlConnectionFactory connectionFactory,
            ILogger<SettingsController> logger)
            : this(settingsService, deliveryBoyService, null, connectionFactory, logger)
        {
        }

        [ActivatorUtilitiesConstructor]
        public SettingsController(
            ISettingsService settingsService,
            IDeliveryBoyService deliveryBoyService,
            IUserAccountService? userAccountService,
            ISqlConnectionFactory connectionFactory,
            ILogger<SettingsController> logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _deliveryBoyService = deliveryBoyService ?? throw new ArgumentNullException(nameof(deliveryBoyService));
            _userAccountService = userAccountService;
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Claims Identity & Cookie Refresh

        private async Task RefreshUserClaimsAsync(AuthenticatedUser user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.GivenName, user.FullName),
                new(ClaimTypes.MobilePhone, user.Phone),
                new(ClaimTypes.Role, user.Role)
            };

            var existingAuth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            bool isPersistent = existingAuth.Properties?.IsPersistent ?? false;
            var props = existingAuth.Properties ?? new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = isPersistent ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(10)
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        }

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

        #region Admin Profile & Password Management

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminProfile(CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("AdminLogin", "Auth");
            }

            if (_userAccountService == null)
            {
                TempData["ErrorMessage"] = "Account management service is currently unavailable.";
                return RedirectToAction(nameof(Index));
            }

            var profile = await _userAccountService.GetProfileByIdAsync(userId, ct).ConfigureAwait(false);
            if (profile == null)
            {
                TempData["ErrorMessage"] = "Administrator account record not found.";
                return RedirectToAction(nameof(Index));
            }

            var vm = new AdminProfilePageViewModel
            {
                UserId = profile.UserId,
                Role = profile.Role,
                IsActive = profile.IsActive,
                CreatedAt = profile.CreatedAt,
                LastLoginAt = profile.LastLoginAt,
                ProfileForm = new AdminProfileUpdateModel
                {
                    Username = profile.Username,
                    FullName = profile.FullName,
                    Phone = profile.Phone
                }
            };

            return View(vm);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAdminProfile([Bind(Prefix = "ProfileForm")] AdminProfileUpdateModel model, CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("AdminLogin", "Auth");
            }

            if (_userAccountService == null)
            {
                TempData["ErrorMessage"] = "Account management service is unavailable.";
                return RedirectToAction(nameof(AdminProfile));
            }

            if (!ModelState.IsValid)
            {
                var profile = await _userAccountService.GetProfileByIdAsync(userId, ct).ConfigureAwait(false);
                var vm = new AdminProfilePageViewModel
                {
                    UserId = userId,
                    Role = profile?.Role ?? "Admin",
                    IsActive = profile?.IsActive ?? true,
                    CreatedAt = profile?.CreatedAt ?? DateTime.UtcNow,
                    LastLoginAt = profile?.LastLoginAt,
                    ProfileForm = model
                };
                return View("AdminProfile", vm);
            }

            var result = await _userAccountService.UpdateAdminProfileAsync(userId, model, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.ErrorMessage ?? "Could not update profile.";
                return RedirectToAction(nameof(AdminProfile));
            }

            if (result.UpdatedUser != null)
            {
                await RefreshUserClaimsAsync(result.UpdatedUser);
            }

            TempData["SuccessMessage"] = result.Message ?? "Profile updated successfully.";
            return RedirectToAction(nameof(AdminProfile));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeAdminPassword([Bind(Prefix = "PasswordForm")] ChangePasswordViewModel model, CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("AdminLogin", "Auth");
            }

            if (_userAccountService == null)
            {
                TempData["ErrorMessage"] = "Account management service is unavailable.";
                return RedirectToAction(nameof(AdminProfile));
            }

            if (!ModelState.IsValid)
            {
                var profile = await _userAccountService.GetProfileByIdAsync(userId, ct).ConfigureAwait(false);
                var vm = new AdminProfilePageViewModel
                {
                    UserId = userId,
                    Role = profile?.Role ?? "Admin",
                    IsActive = profile?.IsActive ?? true,
                    CreatedAt = profile?.CreatedAt ?? DateTime.UtcNow,
                    LastLoginAt = profile?.LastLoginAt,
                    ProfileForm = new AdminProfileUpdateModel
                    {
                        Username = profile?.Username ?? string.Empty,
                        FullName = profile?.FullName ?? string.Empty,
                        Phone = profile?.Phone ?? string.Empty
                    },
                    PasswordForm = new ChangePasswordViewModel()
                };
                return View("AdminProfile", vm);
            }

            var result = await _userAccountService.ChangeOwnPasswordAsync(userId, model, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.ErrorMessage ?? "Password change failed.";
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Password changed successfully.";
            }

            return RedirectToAction(nameof(AdminProfile));
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

        [HttpPost("Settings/Profile/UpdateProfile")]
        [Authorize(Roles = "DeliveryBoy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDeliveryBoyProfile(DeliveryBoyProfileUpdateModel model, CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            if (_userAccountService == null)
            {
                TempData["ErrorMessage"] = "Account management service is unavailable.";
                return RedirectToAction(nameof(Profile));
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please provide a valid Full Name and 10-digit registered mobile number.";
                return RedirectToAction(nameof(Profile));
            }

            var result = await _userAccountService.UpdateDeliveryBoyProfileAsync(userId, model, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.ErrorMessage ?? "Failed to update profile.";
            }
            else
            {
                if (result.UpdatedUser != null)
                {
                    await RefreshUserClaimsAsync(result.UpdatedUser);
                }
                TempData["SuccessMessage"] = result.Message ?? "Your profile details have been updated.";
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpPost("Settings/Profile/ChangePassword")]
        [Authorize(Roles = "DeliveryBoy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeDeliveryBoyPassword(ChangePasswordViewModel model, CancellationToken ct)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            if (_userAccountService == null)
            {
                TempData["ErrorMessage"] = "Account management service is unavailable.";
                return RedirectToAction(nameof(Profile));
            }

            if (!ModelState.IsValid)
            {
                var errors = System.Linq.Enumerable.SelectMany(ModelState.Values, v => v.Errors);
                var firstError = System.Linq.Enumerable.FirstOrDefault(errors)?.ErrorMessage;
                TempData["ErrorMessage"] = firstError ?? "Please enter valid current and matching new passwords (minimum 8 characters).";
                return RedirectToAction(nameof(Profile));
            }

            var result = await _userAccountService.ChangeOwnPasswordAsync(userId, model, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.ErrorMessage ?? "Failed to update password.";
            }
            else
            {
                TempData["SuccessMessage"] = result.Message ?? "Your password has been changed successfully.";
            }

            return RedirectToAction(nameof(Profile));
        }

        #endregion
    }
}
