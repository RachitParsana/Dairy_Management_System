using System;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Represents a recurring product subscription line item for a customer.
    /// Supports multi-category configurations across shifts and schedules.
    /// </summary>
    public class CustomerSubscriptionModel
    {
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Shift { get; set; } = "Morning"; // Morning, Evening, Both
        public decimal PackagingQuantity { get; set; }
        public decimal DailyLiquidVolumeLitres { get; set; }
        public string DeliverySchedule { get; set; } = "Daily"; // Daily, Weekdays, Weekends, CustomDays
        public string DeliveryDays { get; set; } = "1,2,3,4,5,6,7"; // ISO weekdays (1=Mon, 7=Sun)
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public bool IsActive { get; set; } = true;
        public decimal EffectivePricePerUnit { get; set; }
    }
}
