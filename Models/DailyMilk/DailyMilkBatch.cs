using System;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents a delivery session / runsheet batch for a delivery boy on a specific date and shift.
    /// Maps to database table dbo.DailyDeliveryBatches.
    /// Manages the transition between Draft (work-in-progress) and Submitted (locked) states.
    /// </summary>
    public class DailyMilkBatch
    {
        /// <summary>
        /// Surrogate primary key.
        /// </summary>
        public long BatchId { get; set; }

        /// <summary>
        /// Foreign key referencing the delivery boy in dbo.DeliveryBoys.
        /// </summary>
        public int DeliveryBoyId { get; set; }

        /// <summary>
        /// Foreign key referencing the distribution route in dbo.Routes.
        /// </summary>
        public int RouteId { get; set; }

        /// <summary>
        /// Calendar date of the delivery shift.
        /// </summary>
        public DateOnly EntryDate { get; set; }

        /// <summary>
        /// Shift of delivery ('Morning' or 'Evening').
        /// </summary>
        public string Shift { get; set; } = "Morning";

        /// <summary>
        /// Batch lifecycle state: 'Draft' (in-progress) or 'Submitted' (locked for field modification).
        /// </summary>
        public string Status { get; set; } = "Draft";

        /// <summary>
        /// Total number of customers assigned to this delivery boy on this date and shift.
        /// </summary>
        public int TotalAssignedCustomers { get; set; }

        /// <summary>
        /// Number of customer deliveries marked 'Completed'.
        /// </summary>
        public int CompletedCount { get; set; }

        /// <summary>
        /// Number of customer deliveries currently in 'Pending' status.
        /// Must be 0 for final submission.
        /// </summary>
        public int PendingCount { get; set; }

        /// <summary>
        /// Number of customer deliveries marked 'Not Delivered' (including vacations and absences).
        /// </summary>
        public int NotDeliveredCount { get; set; }

        /// <summary>
        /// Total liquid milk volume in litres successfully delivered across all completed entries in this batch.
        /// </summary>
        public decimal TotalLiquidVolumeLitres { get; set; }

        /// <summary>
        /// Total monetary value of completed deliveries in this batch.
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Timestamp when the batch was first initiated (UTC).
        /// </summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when the batch was finalized and submitted by the delivery boy (UTC).
        /// </summary>
        public DateTime? SubmittedAt { get; set; }

        /// <summary>
        /// User who submitted the batch (Delivery Boy or Admin).
        /// </summary>
        public int? SubmittedBy { get; set; }

        /// <summary>
        /// Timestamp when the batch record was created (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User who created the batch record.
        /// </summary>
        public int CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the batch record was last updated (UTC).
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// User who last updated the batch record.
        /// </summary>
        public int? UpdatedBy { get; set; }
    }
}
