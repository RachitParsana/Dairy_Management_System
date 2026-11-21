using System.Collections.Generic;
using Dairy_Management_System.Models.Notification;

namespace Dairy_Management_System.Models.Dashboard
{
    public class DeliveryBoyDashboardViewModel
    {
        public int DeliveryBoyId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public int AssignedRouteId { get; set; }
        public string AssignedRouteName { get; set; } = string.Empty;
        public int TotalRouteCustomers { get; set; }
        public decimal TodayDeliveredLitres { get; set; }
        public int CompletedDropsToday { get; set; }
        public int PendingDropsToday { get; set; }
        public int UnreadNotificationsCount { get; set; }

        // One-time popup notifications for newly published route bills
        public List<NotificationListItemDto> UnacknowledgedBillPopups { get; set; } = new();
    }
}
