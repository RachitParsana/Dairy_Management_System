using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Request model for submitting or drafting daily deliveries for a delivery boy's shift.
    /// Used by POST /DailyMilk/SaveDraft and POST /DailyMilk/Submit.
    /// </summary>
    public class DailyMilkSubmissionViewModel
    {
        [Required(ErrorMessage = "Entry date is required.")]
        public DateOnly EntryDate { get; set; }

        [Required(ErrorMessage = "Shift is required.")]
        [RegularExpression("^(Morning|Evening)$", ErrorMessage = "Shift must be 'Morning' or 'Evening'.")]
        public string Shift { get; set; } = "Morning";

        /// <summary>
        /// Delivery boy executing or submitting the entries.
        /// Server-side validation strictly verifies that the logged-in Delivery Boy can only submit for their own DeliveryBoyId.
        /// </summary>
        [Required(ErrorMessage = "Delivery boy identifier is required.")]
        public int DeliveryBoyId { get; set; }

        /// <summary>
        /// Optional route identifier for verification.
        /// </summary>
        public int? RouteId { get; set; }

        /// <summary>
        /// True for final shift submission (stamps batch as Submitted, captures commission rate snapshot);
        /// False for saving a draft runsheet.
        /// Note: Submitting a batch preserves any remaining 'Pending' entries as 'Pending'; they are NOT automatically converted.
        /// </summary>
        public bool IsFinalSubmission { get; set; }

        /// <summary>
        /// Collection of customer delivery records included in the runsheet submission.
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one customer delivery entry must be provided.")]
        public List<DailyMilkCustomerInputModel> CustomerEntries { get; set; } = new();
    }

    /// <summary>
    /// Input record for a single customer within a batch submission.
    /// </summary>
    public class DailyMilkCustomerInputModel
    {
        [Required]
        public int CustomerId { get; set; }

        /// <summary>
        /// Delivery status: 'Completed', 'Pending', or 'Not Delivered'.
        /// If a batch is submitted with Status = 'Pending', the record is preserved as 'Pending'.
        /// 'Pending' records generate ₹0.00 milk charges and 0.000 eligible litres for commission.
        /// </summary>
        [Required]
        [RegularExpression("^(Completed|Pending|Not Delivered)$", ErrorMessage = "Status must be 'Completed', 'Pending', or 'Not Delivered'.")]
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Reason if Status is 'Not Delivered' (e.g., 'Customer Absent', 'Door Locked', 'Refused').
        /// </summary>
        [StringLength(250, ErrorMessage = "Non-delivery reason cannot exceed 250 characters.")]
        public string? NonDeliveryReason { get; set; }

        /// <summary>
        /// True if this entry represents an Admin-authorized unscheduled delivery on an off-schedule date.
        /// </summary>
        public bool IsUnscheduledDelivery { get; set; }

        /// <summary>
        /// Delivered packaging quantities broken down by dairy product category.
        /// </summary>
        public List<DailyMilkCategoryInputModel> CategoryQuantities { get; set; } = new();
    }

    /// <summary>
    /// Input record for a category quantity within a customer's delivery.
    /// </summary>
    public class DailyMilkCategoryInputModel
    {
        [Required]
        public int CategoryId { get; set; }

        /// <summary>
        /// Delivered physical packaging units (e.g. number of pouches, bottles, packs, tubs).
        /// Must be >= 0. Must be exactly 0 if customer Status is 'Not Delivered'.
        /// </summary>
        [Range(0, 1000, ErrorMessage = "Packaging quantity must be between 0 and 1000.")]
        public decimal PackagingQuantity { get; set; }
    }
}
