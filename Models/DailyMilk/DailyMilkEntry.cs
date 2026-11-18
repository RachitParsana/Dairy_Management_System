using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents the daily delivery header record per customer, date, and shift.
    /// Maps to database table dbo.DailyMilkDeliveries.
    /// Strictly enforces the three mandatory statuses: 'Completed', 'Pending', and 'Not Delivered'.
    /// Note: Vacation is NOT a delivery status. Approved vacations block delivery and are tracked in dbo.CustomerVacations.
    /// </summary>
    public class DailyMilkEntry
    {
        /// <summary>
        /// Surrogate primary key.
        /// </summary>
        public long DeliveryId { get; set; }

        /// <summary>
        /// Foreign key referencing the shift delivery batch in dbo.DailyDeliveryBatches.
        /// </summary>
        public long? BatchId { get; set; }

        /// <summary>
        /// Foreign key referencing the customer in dbo.Customers.
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// Foreign key referencing the delivery boy in dbo.DeliveryBoys who executed or was assigned to the delivery.
        /// </summary>
        public int DeliveryBoyId { get; set; }

        /// <summary>
        /// Calendar date of milk distribution.
        /// </summary>
        public DateOnly EntryDate { get; set; }

        /// <summary>
        /// Delivery shift ('Morning' or 'Evening').
        /// </summary>
        public string Shift { get; set; } = "Morning";

        /// <summary>
        /// Mandatory delivery status: 'Completed', 'Pending', or 'Not Delivered'.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Optional reference to dbo.CustomerVacations if non-delivery is linked to an approved vacation.
        /// Note: Synthetic delivery records are NOT created simply to represent vacations.
        /// </summary>
        public int? VacationId { get; set; }

        /// <summary>
        /// Explanatory reason when Status is 'Not Delivered' (e.g., 'Customer Absent', 'Door Locked', 'Refused').
        /// </summary>
        public string? NonDeliveryReason { get; set; }

        /// <summary>
        /// True if this delivery was an Admin-authorized unscheduled delivery on an off-schedule date.
        /// </summary>
        public bool IsUnscheduledDelivery { get; set; }

        /// <summary>
        /// Foreign key referencing the Admin in dbo.Users who authorized an unscheduled delivery exception.
        /// </summary>
        public int? AdminAuthorizedBy { get; set; }

        /// <summary>
        /// Snapshot of the delivery boy's active per-litre commission rate on EntryDate.
        /// Set to 0.00 for fixed-salary personnel.
        /// </summary>
        public decimal CommissionRateSnapshot { get; set; }

        /// <summary>
        /// Daily aggregate converted liquid milk volume delivered in litres across liquid items.
        /// Strictly 0.000 when Status is 'Not Delivered' or 'Pending'.
        /// </summary>
        public decimal TotalLiquidVolumeLitres { get; set; }

        /// <summary>
        /// Eligible liquid milk volume qualifying for commission calculation.
        /// Strictly equal to TotalLiquidVolumeLitres when Status is 'Completed' and customer is not on approved vacation; 0.000 otherwise.
        /// </summary>
        public decimal EligibleLitreQuantity { get; set; }

        /// <summary>
        /// Daily aggregate monetary charge across all category items = SUM(Items.SubTotal).
        /// Strictly 0.00 when Status is 'Not Delivered' or 'Pending'.
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Indicates whether this delivery has been corrected by an Administrator after initial submission.
        /// </summary>
        public bool IsCorrected { get; set; }

        /// <summary>
        /// Timestamp of the most recent administrative correction (UTC).
        /// </summary>
        public DateTime? LastCorrectedAt { get; set; }

        /// <summary>
        /// Foreign key referencing the Admin in dbo.Users who authorized the correction.
        /// </summary>
        public int? LastCorrectedBy { get; set; }

        /// <summary>
        /// Record creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User who created the record (Delivery Boy or Admin).
        /// </summary>
        public int CreatedBy { get; set; }

        /// <summary>
        /// Record last modification timestamp (UTC).
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// User who last modified the record.
        /// </summary>
        public int? UpdatedBy { get; set; }

        /// <summary>
        /// Compositional line items broken down by dairy product category.
        /// </summary>
        public List<DailyMilkCategoryEntry> Items { get; set; } = new();
    }
}
