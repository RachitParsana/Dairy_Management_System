using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Search and filter criteria for querying daily delivery records via continuous scrolling feed.
    /// Strictly cursor-based without conventional page-number pagination.
    /// Uses a deterministic composite cursor (CursorEntryDate, CursorShift, CursorDeliveryId)
    /// to support robust multi-date and multi-shift lexicographic scrolling.
    /// </summary>
    public class DailyMilkFilterCriteria
    {
        /// <summary>
        /// Specific calendar date for daily delivery lookup.
        /// </summary>
        public DateOnly? EntryDate { get; set; }

        /// <summary>
        /// Optional date range start filter.
        /// </summary>
        public DateOnly? StartDate { get; set; }

        /// <summary>
        /// Optional date range end filter.
        /// </summary>
        public DateOnly? EndDate { get; set; }

        /// <summary>
        /// Distribution shift filter ('Morning', 'Evening', or null for all).
        /// </summary>
        public string? Shift { get; set; }

        /// <summary>
        /// Geographic delivery route filter.
        /// </summary>
        public int? RouteId { get; set; }

        /// <summary>
        /// Delivery personnel filter.
        /// </summary>
        public int? DeliveryBoyId { get; set; }

        /// <summary>
        /// Customer filter.
        /// </summary>
        public int? CustomerId { get; set; }

        /// <summary>
        /// Delivery status filter ('Completed', 'Pending', 'Not Delivered', or null for all).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Filter by whether the entry has been corrected by an Admin.
        /// </summary>
        public bool? IsCorrected { get; set; }

        /// <summary>
        /// Filter by unscheduled delivery exceptions.
        /// </summary>
        public bool? IsUnscheduled { get; set; }

        /// <summary>
        /// Batch status filter ('Draft', 'Submitted').
        /// </summary>
        public string? BatchStatus { get; set; }

        /// <summary>
        /// Search term matching customer name, customer code, or phone number.
        /// </summary>
        public string? SearchTerm { get; set; }

        #region Composite Continuous Scrolling Cursor

        /// <summary>
        /// Composite Cursor Part 1: Calendar date of the last item in the previous continuous chunk.
        /// </summary>
        public DateOnly? CursorEntryDate { get; set; }

        /// <summary>
        /// Composite Cursor Part 2: Distribution shift of the last item in the previous continuous chunk ('Evening' or 'Morning').
        /// </summary>
        public string? CursorShift { get; set; }

        /// <summary>
        /// Composite Cursor Part 3: Primary key DeliveryId of the last item in the previous continuous chunk.
        /// </summary>
        public long? CursorDeliveryId { get; set; }

        /// <summary>
        /// Number of records to stream in the continuous scroll increment (default 25).
        /// </summary>
        public int FetchLimit { get; set; } = 25;

        /// <summary>
        /// Evaluates whether a complete composite cursor position has been supplied.
        /// </summary>
        public bool HasCompositeCursor =>
            CursorEntryDate.HasValue && !string.IsNullOrWhiteSpace(CursorShift) && CursorDeliveryId.HasValue;

        #endregion
    }

    /// <summary>
    /// Response envelope for continuous scrolling endpoints in the Daily Milk module.
    /// Strictly eliminates PageNumber and PageSize from public contracts.
    /// Uses composite next-cursor properties for deterministic lexicographic continuation.
    /// </summary>
    public class DailyMilkContinuousScrollResult<T>
    {
        /// <summary>
        /// Incremental items fetched for the current continuous scroll chunk.
        /// </summary>
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

        /// <summary>
        /// Total record count matching criteria (for summary badges and statistics).
        /// </summary>
        public int TotalMatchingCount { get; set; }

        /// <summary>
        /// True if additional items exist beyond the current cursor position.
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// Composite Next Cursor Part 1: EntryDate of the last item in the returned chunk.
        /// </summary>
        public DateOnly? NextCursorEntryDate { get; set; }

        /// <summary>
        /// Composite Next Cursor Part 2: Shift of the last item in the returned chunk.
        /// </summary>
        public string? NextCursorShift { get; set; }

        /// <summary>
        /// Composite Next Cursor Part 3: DeliveryId of the last item in the returned chunk.
        /// </summary>
        public long? NextCursorDeliveryId { get; set; }

        /// <summary>
        /// Backward-compatible alias for NextCursorDeliveryId.
        /// </summary>
        public long? NextCursor
        {
            get => NextCursorDeliveryId;
            set => NextCursorDeliveryId = value;
        }
    }
}
