using System;

namespace Dairy_Management_System.Models.Notification
{
    public class Notification
    {
        public long NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = "GeneralAnnouncement"; // BillPublished, BillUpdated, GeneralAnnouncement, VacationUpdate
        public string TargetRole { get; set; } = "All"; // All, DeliveryBoy, Admin
        public int? TargetRouteId { get; set; }
        public int? TargetDeliveryBoyId { get; set; }
        public int? CustomerId { get; set; }
        public long? BillId { get; set; }
        public string? CustomerNameSnapshot { get; set; }
        public decimal? UpdatedBillAmount { get; set; }
        public decimal? NewTotalOutstandingBalance { get; set; }
        public string? BillingMonthYear { get; set; }
        public int? PendingMonthsCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
    }

    public class NotificationAcknowledgement
    {
        public long AcknowledgementId { get; set; }
        public long NotificationId { get; set; }
        public int UserId { get; set; }
        public DateTime AcknowledgedAt { get; set; }
    }

    public class NotificationListItemDto
    {
        public long NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = string.Empty;
        public string TargetRole { get; set; } = string.Empty;
        public int? TargetRouteId { get; set; }
        public string? TargetRouteName { get; set; }
        public int? TargetDeliveryBoyId { get; set; }
        public string? TargetDeliveryBoyName { get; set; }
        public int? CustomerId { get; set; }
        public long? BillId { get; set; }
        public string? CustomerNameSnapshot { get; set; }
        public decimal? UpdatedBillAmount { get; set; }
        public decimal? NewTotalOutstandingBalance { get; set; }
        public string? BillingMonthYear { get; set; }
        public int? PendingMonthsCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public int AcknowledgementCount { get; set; }
    }

    public class CreateNotificationViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = "GeneralAnnouncement";
        public string TargetRole { get; set; } = "All";
        public int? TargetRouteId { get; set; }
        public int? TargetDeliveryBoyId { get; set; }
    }
}
