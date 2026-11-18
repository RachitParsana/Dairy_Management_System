using System;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Audit log record capturing an individual administrative correction to a daily delivery.
    /// Maps to database table dbo.DailyMilkDeliveryCorrections.
    /// Granular line-item audit: multiple category modifications in one Admin session share a common CorrectionBatchGuid.
    /// </summary>
    public class DailyMilkCorrectionRecord
    {
        /// <summary>
        /// Surrogate primary key.
        /// </summary>
        public long CorrectionId { get; set; }

        /// <summary>
        /// Grouping identifier linking all line-item changes made during the same administrative correction transaction.
        /// </summary>
        public Guid CorrectionBatchGuid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Foreign key referencing the parent delivery header in dbo.DailyMilkDeliveries.
        /// </summary>
        public long DeliveryId { get; set; }

        /// <summary>
        /// Foreign key referencing the specific line item in dbo.DailyMilkDeliveryItems if category-level.
        /// Null for header-only status changes.
        /// </summary>
        public long? DeliveryItemId { get; set; }

        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerCode { get; set; }

        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = "Morning";

        public string PriorStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;

        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }

        public decimal? PriorPackagingQuantity { get; set; }
        public decimal? NewPackagingQuantity { get; set; }

        public decimal PriorLiquidVolumeLitres { get; set; }
        public decimal NewLiquidVolumeLitres { get; set; }

        public decimal? PriorUnitPrice { get; set; }
        public decimal? NewUnitPrice { get; set; }

        public decimal PriorTotalAmount { get; set; }
        public decimal NewTotalAmount { get; set; }

        /// <summary>
        /// Mandatory business justification explaining why the correction was made.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key referencing the Admin in dbo.Users who executed the correction.
        /// </summary>
        public int CorrectedBy { get; set; }
        public string? CorrectedByName { get; set; }

        /// <summary>
        /// Timestamp when correction was executed (UTC).
        /// </summary>
        public DateTime CorrectedAt { get; set; }
    }
}
