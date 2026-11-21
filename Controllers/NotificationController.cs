using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Notification;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Controllers
{
    public class NotificationController : Controller
    {
        private readonly INotificationService _notificationService;
        private readonly IRouteService _routeService;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            INotificationService notificationService,
            IRouteService routeService,
            ILogger<NotificationController> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Admin Notification Hub

        /// <summary>
        /// Admin Notification Hub: overview of broadcast notices and audit ledger.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(CancellationToken ct = default)
        {
            try
            {
                var noticesTask = _notificationService.GetAdminOverviewAsync(50, ct);
                var routesTask = _routeService.GetActiveRoutesLookupAsync(ct);

                await Task.WhenAll(noticesTask, routesTask).ConfigureAwait(false);

                ViewBag.Routes = await routesTask;
                var notices = await noticesTask;
                return View(notices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Admin Notification Console.");
                ViewBag.Routes = Array.Empty<Dairy_Management_System.Models.Route.RouteLookupModel>();
                return View(Array.Empty<NotificationListItemDto>());
            }
        }

        /// <summary>
        /// Broadcasts an announcement targeting staff or route delivery boys.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Broadcast(CreateNotificationViewModel model, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please provide all required broadcast message fields.";
                return RedirectToAction(nameof(Index));
            }

            int adminUserId = 1;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(userIdClaim) && int.TryParse(userIdClaim, out var parsedId))
            {
                adminUserId = parsedId;
            }

            try
            {
                var notifId = await _notificationService.SendBroadcastAsync(model, adminUserId, ct).ConfigureAwait(false);
                TempData["SuccessMessage"] = $"Broadcast alert dispatched successfully (Ref #{notifId}).";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch broadcast notification.");
                TempData["ErrorMessage"] = "Failed to dispatch broadcast notice. Error logged.";
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion

        #region Delivery Boy Route Alerts

        /// <summary>
        /// Delivery Boy Notification Stream: alerts and published bill notifications.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> DeliveryBoyNotifications(CancellationToken ct = default)
        {
            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return RedirectToAction("DeliveryBoyLogin", "Auth");
            }

            try
            {
                var alerts = await _notificationService.GetUserNotificationsAsync(userId, "DeliveryBoy", null, null, ct).ConfigureAwait(false);
                return View(alerts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load notifications for Delivery Boy UserId: {UserId}", userId);
                return View(Array.Empty<NotificationListItemDto>());
            }
        }

        /// <summary>
        /// Acknowledges a notification (marks it read/seen).
        /// Strictly records read status without altering any billing, balance, or payment records.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> Acknowledge(long notificationId, CancellationToken ct = default)
        {
            if (notificationId <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid notification identifier." });
            }

            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new { success = false, message = "User identity could not be verified." });
            }

            try
            {
                var success = await _notificationService.AcknowledgeNotificationAsync(notificationId, userId, ct).ConfigureAwait(false);
                return Json(new { success });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acknowledge NotificationId: {NotifId} for UserId: {UserId}", notificationId, userId);
                return Json(new { success = false, message = "Internal error recording read receipt." });
            }
        }

        /// <summary>
        /// Marks a publication popup as seen for this partner so it does not reappear on future logins.
        /// Persists permanently into dbo.NotificationPopupSeen via ownership chaining.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "DeliveryBoy")]
        public async Task<IActionResult> DismissPopup(long notificationId, CancellationToken ct = default)
        {
            if (notificationId <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid notification identifier." });
            }

            int userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new { success = false, message = "User identity could not be verified." });
            }

            try
            {
                var success = await _notificationService.MarkPopupSeenAsync(notificationId, userId, ct).ConfigureAwait(false);
                return Json(new { success });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark popup seen for NotificationId: {NotifId} for UserId: {UserId}", notificationId, userId);
                return Json(new { success = false, message = "Internal error dismissing popup." });
            }
        }

        #endregion

        #region Shared Endpoints

        /// <summary>
        /// Returns live unread notification count for authenticated user's topbar badge polling.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount(CancellationToken ct = default)
        {
            int userId = GetCurrentUserId();
            string role = User.IsInRole("Admin") ? "Admin" : "DeliveryBoy";

            try
            {
                var count = await _notificationService.GetUnreadCountAsync(userId, role, null, null, ct).ConfigureAwait(false);
                return Json(new { unreadCount = count });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling unread count for UserId: {UserId}", userId);
                return Json(new { unreadCount = 0 });
            }
        }

        #endregion

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var parsedId) ? parsedId : 0;
        }
    }
}
