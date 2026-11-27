using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Notification;

namespace Dairy_Management_System.Services
{
    public interface INotificationService
    {
        Task<long> SendBroadcastAsync(CreateNotificationViewModel model, int adminUserId, CancellationToken ct = default);
        Task<long> EmitBillPublishedNotificationAsync(long billId, int adminUserId, CancellationToken ct = default);
        Task<int> ReconcilePublishedBillNotificationsAsync(int adminUserId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetUserNotificationsAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default);
        Task<bool> AcknowledgeNotificationAsync(long notificationId, int userId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetUnacknowledgedBillPopupsAsync(int deliveryBoyId, int userId, CancellationToken ct = default);
        Task<bool> MarkPopupSeenAsync(long notificationId, int userId, CancellationToken ct = default);
        Task<bool> MarkAllReadAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default);
        Task<int> GetUnreadCountAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetAdminOverviewAsync(int limit = 50, CancellationToken ct = default);
    }
}
