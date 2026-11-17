using System.Security.Claims;
using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dairy_Management_System.Controllers
{
    /// <summary>
    /// Authentication controller for staff accounts (Admin and Delivery Boy).
    /// Enforces separate login flows, secure cookie issuance, safe ReturnUrl redirection,
    /// anti-forgery protected signout, and role transition isolation.
    /// </summary>
    public class AuthController : Controller
    {
        private readonly IUserAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        #region Admin Login

        /// <summary>
        /// Displays the Administrator login portal.
        /// If already authenticated as Admin, redirects to the Admin dashboard.
        /// If authenticated as another role, renders the view with an explicit role-switching prompt.
        /// Strictly idempotent: GET requests NEVER terminate or alter authenticated sessions.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AdminLogin(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            {
                return RedirectToLocal(returnUrl, "Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new AdminLoginViewModel());
        }

        /// <summary>
        /// Authenticates Administrator credentials (Username + Phone + Password).
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminLogin(AdminLoginViewModel model, string? returnUrl = null, CancellationToken cancellationToken = default)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _authService.AuthenticateAdminAsync(model.Username, model.Phone, model.Password, cancellationToken);
            if (!result.Succeeded || result.User == null)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Invalid credentials. Please verify your login details.");
                return View(model);
            }

            await SignInUserAsync(result.User, model.RememberMe);
            _logger.LogInformation("Admin user {Username} signed in successfully from IP: {IpAddress}.", result.User.Username, HttpContext.Connection.RemoteIpAddress);

            return RedirectToLocal(returnUrl, "Index", "Home");
        }

        #endregion

        #region Delivery Boy Login

        /// <summary>
        /// Displays the Delivery Boy mobile-first login portal.
        /// If already authenticated as Delivery Boy, redirects to the Delivery Boy dashboard.
        /// If authenticated as another role, renders the view with an explicit role-switching prompt.
        /// Strictly idempotent: GET requests NEVER terminate or alter authenticated sessions.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult DeliveryBoyLogin(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("DeliveryBoy"))
            {
                return RedirectToLocal(returnUrl, "Privacy", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new DeliveryBoyLoginViewModel());
        }

        /// <summary>
        /// Authenticates Delivery Boy credentials (Phone + Password).
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeliveryBoyLogin(DeliveryBoyLoginViewModel model, string? returnUrl = null, CancellationToken cancellationToken = default)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _authService.AuthenticateDeliveryBoyAsync(model.Phone, model.Password, cancellationToken);
            if (!result.Succeeded || result.User == null)
            {
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Invalid credentials. Please verify your login details.");
                return View(model);
            }

            await SignInUserAsync(result.User, model.RememberMe);
            _logger.LogInformation("Delivery boy {Phone} signed in successfully from IP: {IpAddress}.", result.User.Phone, HttpContext.Connection.RemoteIpAddress);

            return RedirectToLocal(returnUrl, "Privacy", "Home");
        }

        #endregion

        #region Explicit Role Switching

        /// <summary>
        /// Explicit role-switching endpoint.
        /// Terminates the current authenticated session and redirects to the requested role's login portal.
        /// Strictly requires authentication, POST method, and Anti-Forgery Token validation.
        /// </summary>
        /// <param name="targetRole">Target destination role ('Admin' or 'DeliveryBoy').</param>
        /// <param name="returnUrl">Optional local return destination preserved across login.</param>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchRole([FromForm] string targetRole, [FromForm] string? returnUrl = null)
        {
            var username = User.Identity?.Name ?? "Unknown";

            // Enforce strictly allowed destination roles
            if (string.Equals(targetRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                _logger.LogInformation("User {Username} executed role switch to Admin. Previous session terminated.", username);

                var safeReturnUrl = (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) ? returnUrl : null;
                return RedirectToAction(nameof(AdminLogin), new { returnUrl = safeReturnUrl });
            }

            if (string.Equals(targetRole, "DeliveryBoy", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                _logger.LogInformation("User {Username} executed role switch to DeliveryBoy. Previous session terminated.", username);

                var safeReturnUrl = (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) ? returnUrl : null;
                return RedirectToAction(nameof(DeliveryBoyLogin), new { returnUrl = safeReturnUrl });
            }

            _logger.LogWarning("Rejecting invalid SwitchRole target '{TargetRole}' requested by user {Username}.", targetRole, username);
            return BadRequest("Invalid target role specified for role switching.");
        }

        #endregion

        #region Logout

        /// <summary>
        /// Signs out the authenticated user.
        /// Strictly requires POST method and Anti-Forgery Token verification.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var isDeliveryBoy = User.IsInRole("DeliveryBoy");
            var username = User.Identity?.Name ?? "Unknown";

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User {Username} signed out.", username);

            if (isDeliveryBoy)
            {
                return RedirectToAction(nameof(DeliveryBoyLogin));
            }

            return RedirectToAction(nameof(AdminLogin));
        }

        #endregion

        #region Access Denied

        /// <summary>
        /// Displays the 403 Forbidden Access Denied page when an authenticated user lacks the required role.
        /// Explicitly emits HTTP 403 Forbidden status code while rendering view.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Issues an encrypted authentication cookie populated with security-safe claims.
        /// Explicitly executes SignOutAsync first to clear any preexisting session before issuing new claims.
        /// PasswordHash is strictly NEVER included.
        /// </summary>
        private async Task SignInUserAsync(AuthenticatedUser user, bool rememberMe)
        {
            // Clear any preexisting authentication ticket or stale cross-role claims
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.GivenName, user.FullName),
                new(ClaimTypes.MobilePhone, user.Phone),
                new(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(7)
                    : DateTimeOffset.UtcNow.AddHours(10)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                authProperties);
        }

        /// <summary>
        /// Safely validates ReturnUrl to prevent Open Redirect attacks.
        /// Only relative local URLs are accepted; external or invalid URLs fall back to the designated role dashboard.
        /// </summary>
        private IActionResult RedirectToLocal(string? returnUrl, string defaultAction, string defaultController)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(defaultAction, defaultController);
        }

        #endregion
    }
}
