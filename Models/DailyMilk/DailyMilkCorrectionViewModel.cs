using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Request model for Admin-authorized correction of an existing daily delivery record.
    /// Strictly authorized for users in the 'Admin' role.
    /// Requires an explicit, non-empty business justification and records an immutable audit log.
    /// </summary>
    public class DailyMilkCorrectionViewModel
    {
        [Required(ErrorMessage = "Delivery identifier is required.")]
        public long DeliveryId { get; set; }

        /// <summary>
        /// Optional line item identifier if correcting a single category line item.
        /// </summary>
        public long? DeliveryItemId { get; set; }

        /// <summary>
        /// New delivery status: 'Completed', 'Pending', or 'Not Delivered'.
        /// </summary>
        [Required]
        [RegularExpression("^(Completed|Pending|Not Delivered)$", ErrorMessage = "Status must be 'Completed', 'Pending', or 'Not Delivered'.")]
        public string NewStatus { get; set; } = "Completed";

        /// <summary>
        /// Non-delivery reason when NewStatus is 'Not Delivered'.
        /// </summary>
        [StringLength(250, ErrorMessage = "Non-delivery reason cannot exceed 250 characters.")]
        public string? NonDeliveryReason { get; set; }

        /// <summary>
        /// Updated category quantities.
        /// </summary>
        public List<DailyMilkCategoryInputModel> CategoryQuantities { get; set; } = new();

        /// <summary>
        /// Mandatory business justification explaining why the correction is being made.
        /// (e.g., 'Customer reported delivery boy error', 'Incorrect quantity entered on route').
        /// </summary>
        [Required(ErrorMessage = "Correction reason is mandatory.")]
        [MinLength(5, ErrorMessage = "Correction reason must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Correction reason cannot exceed 250 characters.")]
        public string CorrectionReason { get; set; } = string.Empty;
    }
}
