using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Notification;
using Dairy_Management_System.Repositories;

namespace Dairy_Management_System.Services
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;

        public NotificationService(INotificationRepository notificationRepository)
        {
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        }

        public async Task<long> SendBroadcastAsync(CreateNotificationViewModel model, int adminUserId, CancellationToken ct = default)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(model.Title)) throw new ArgumentException("Title is required.", nameof(model.Title));
            if (string.IsNullOrWhiteSpace(model.Message)) throw new ArgumentException("Message is required.", nameof(model.Message));

            var notification = new Notification
            {
                Title = model.Title.Trim(),
                Message = model.Message.Trim(),
                NotificationType = string.IsNullOrWhiteSpace(model.NotificationType) ? "GeneralAnnouncement" : model.NotificationType.Trim(),
                TargetRole = string.IsNullOrWhiteSpace(model.TargetRole) ? "All" : model.TargetRole.Trim(),
                TargetRouteId = model.TargetRouteId,
                TargetDeliveryBoyId = model.TargetDeliveryBoyId,
                CreatedBy = adminUserId
            };

            return await _notificationRepository.CreateAsync(notification, ct);
        }

        public async Task<long> EmitBillPublishedNotificationAsync(long billId, int adminUserId, CancellationToken ct = default)
        {
            if (billId <= 0) throw new ArgumentException("Valid BillId is required.", nameof(billId));
            return await _notificationRepository.EmitBillPublishedAsync(billId, adminUserId, ct);
        }

        public async Task<int> ReconcilePublishedBillNotificationsAsync(int adminUserId, CancellationToken ct = default)
        {
            return await _notificationRepository.ReconcilePublishedBillsAsync(adminUserId, ct);
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetUserNotificationsAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default)
        {
            return await _notificationRepository.GetForUserAsync(userId, role, deliveryBoyId, routeId, ct);
        }

        public async Task<bool> AcknowledgeNotificationAsync(long notificationId, int userId, CancellationToken ct = default)
        {
            if (notificationId <= 0) return false;
            return await _notificationRepository.AcknowledgeAsync(notificationId, userId, ct);
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetUnacknowledgedBillPopupsAsync(int deliveryBoyId, int userId, CancellationToken ct = default)
        {
            return await _notificationRepository.GetUnacknowledgedBillPopupsAsync(deliveryBoyId, userId, ct);
        }

        public async Task<bool> MarkPopupSeenAsync(long notificationId, int userId, CancellationToken ct = default)
        {
            if (notificationId <= 0) return false;
            return await _notificationRepository.MarkPopupSeenAsync(notificationId, userId, ct);
        }

        public async Task<int> GetUnreadCountAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default)
        {
            var notifications = await _notificationRepository.GetForUserAsync(userId, role, deliveryBoyId, routeId, ct);
            return notifications.Count(n => !n.IsAcknowledged);
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetAdminOverviewAsync(int limit = 50, CancellationToken ct = default)
        {
            return await _notificationRepository.GetAdminOverviewAsync(limit, ct);
        }
    }
}
