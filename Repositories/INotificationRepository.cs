using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Notification;

namespace Dairy_Management_System.Repositories
{
    public interface INotificationRepository
    {
        Task<long> CreateAsync(Notification notification, CancellationToken ct = default);
        Task<long> EmitBillPublishedAsync(long billId, int adminUserId, CancellationToken ct = default);
        Task<int> ReconcilePublishedBillsAsync(int adminUserId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetForUserAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default);
        Task<bool> AcknowledgeAsync(long notificationId, int userId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetUnacknowledgedBillPopupsAsync(int deliveryBoyId, int userId, CancellationToken ct = default);
        Task<bool> MarkPopupSeenAsync(long notificationId, int userId, CancellationToken ct = default);
        Task<bool> MarkAllReadAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationListItemDto>> GetAdminOverviewAsync(int limit = 50, CancellationToken ct = default);
    }
}
