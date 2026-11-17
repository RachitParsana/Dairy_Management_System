using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Comprehensive projection model for rendering the full customer profile,
    /// current subscriptions, price agreements, assignment history, and recent vacation logs.
    /// </summary>
    public class CustomerDetailsViewModel
    {
        public Customer Customer { get; set; } = new();
        public string RouteName { get; set; } = string.Empty;
        public string? DeliveryBoyName { get; set; }
        public string? DeliveryBoyPhone { get; set; }
        public bool IsOnVacationToday { get; set; }

        public List<CustomerSubscriptionModel> ActiveSubscriptions { get; set; } = new();
        public List<CustomerCustomPriceModel> CustomPrices { get; set; } = new();
        public List<CustomerAssignmentHistoryModel> AssignmentHistory { get; set; } = new();
        public List<CustomerVacationViewModel> RecentVacations { get; set; } = new();

        public decimal TotalDailyMorningLitres { get; set; }
        public decimal TotalDailyEveningLitres { get; set; }
        public decimal TotalDailyVolumeLitres { get; set; }
        public decimal OutstandingAccountBalance { get; set; }
    }

    /// <summary>
    /// Historical record of delivery boy route assignments for a customer.
    /// </summary>
    public class CustomerAssignmentHistoryModel
    {
        public long AssignmentId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public DateOnly AssignedFrom { get; set; }
        public DateOnly? AssignedTo { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
