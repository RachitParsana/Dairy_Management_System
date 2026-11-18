using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Composite ViewModel for rendering the complete Daily Milk runsheet.
    /// Used by Delivery Boy field entry portal and Admin daily operational console.
    /// </summary>
    public class DailyMilkRosterViewModel
    {
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = "Morning";

        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public string DeliveryBoyPhone { get; set; } = string.Empty;

        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;

        public long? BatchId { get; set; }
        public string BatchStatus { get; set; } = "New"; // New, Draft, Submitted
        public bool IsSubmitted => BatchStatus == "Submitted";
        public DateTime? SubmittedAt { get; set; }
        public string? SubmittedByName { get; set; }

        public int TotalCustomers { get; set; }
        public int CompletedCount { get; set; }
        public int PendingCount { get; set; }
        public int NotDeliveredCount { get; set; }
        public int VacationCount { get; set; }

        public decimal TotalLiquidLitres { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Customer delivery rows ordered by customer delivery sequence.
        /// </summary>
        public List<DailyMilkListItemViewModel> Customers { get; set; } = new();

        /// <summary>
        /// Available active milk categories for dynamic column headers or modal additions.
        /// </summary>
        public List<DailyMilkCategoryHeaderModel> AvailableCategories { get; set; } = new();
    }

    /// <summary>
    /// Metadata describing an active milk category for runsheet column rendering.
    /// </summary>
    public class DailyMilkCategoryHeaderModel
    {
        public int CategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ProductType { get; set; } = "Liquid";
        public string UnitOfMeasure { get; set; } = "Pouch";
        public string PricingBasis { get; set; } = "Per Litre";
        public decimal DefaultPrice { get; set; }
    }
}
