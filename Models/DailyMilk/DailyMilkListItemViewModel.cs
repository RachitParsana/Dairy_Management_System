using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// ViewModel representing a customer's line in the daily delivery runsheet or continuous scroll feed.
    /// Used by Delivery Boy mobile views and Admin daily monitoring grids.
    /// </summary>
    public class DailyMilkListItemViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerNameGujarati { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Area { get; set; }
        public int DeliverySequence { get; set; }

        /// <summary>
        /// DeliveryId if an entry exists in dbo.DailyMilkDeliveries; null if unrecorded.
        /// </summary>
        public long? DeliveryId { get; set; }

        /// <summary>
        /// Delivery status: 'Completed', 'Pending', or 'Not Delivered'.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// True if the customer has an approved vacation in dbo.CustomerVacations covering this date and shift.
        /// When true, the runsheet displays an "Approved Vacation" badge and disables delivery inputs.
        /// No synthetic delivery record is created simply to represent vacation.
        /// </summary>
        public bool IsOnApprovedVacation { get; set; }

        /// <summary>
        /// Reference to the approved vacation ID in dbo.CustomerVacations.
        /// </summary>
        public int? VacationId { get; set; }

        /// <summary>
        /// Vacation reason from dbo.CustomerVacations.
        /// </summary>
        public string? VacationReason { get; set; }

        /// <summary>
        /// Formatted vacation date interval and shift (e.g. "18-Nov to 22-Nov (Morning)") for runsheet badge display.
        /// </summary>
        public string? VacationWindowText { get; set; }

        /// <summary>
        /// True if the customer has a pending (unapproved) vacation request.
        /// Rendered as an informational badge; does NOT block delivery.
        /// </summary>
        public bool HasPendingVacation { get; set; }

        /// <summary>
        /// Non-delivery reason when Status is 'Not Delivered' (e.g., 'Customer Absent', 'Door Locked', 'Refused').
        /// </summary>
        public string? NonDeliveryReason { get; set; }

        /// <summary>
        /// True if this delivery is on an unscheduled date and was authorized by an Administrator.
        /// </summary>
        public bool IsUnscheduledDelivery { get; set; }

        /// <summary>
        /// True if the customer is scheduled for delivery on this date's weekday.
        /// </summary>
        public bool IsScheduledToday { get; set; } = true;

        /// <summary>
        /// True if an entry already exists in the database.
        /// </summary>
        public bool IsExistingEntry { get; set; }

        /// <summary>
        /// True if this entry was corrected by an Admin after submission.
        /// </summary>
        public bool IsCorrected { get; set; }

        /// <summary>
        /// Total liquid volume delivered in litres = SUM(Categories.LiquidVolumeLitres).
        /// </summary>
        public decimal TotalLiquidLitres { get; set; }

        /// <summary>
        /// Total monetary charge = SUM(Categories.SubTotal).
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Itemized category quantities and rates for this customer on this shift.
        /// </summary>
        public List<DailyMilkCategoryItemViewModel> Categories { get; set; } = new();
    }

    /// <summary>
    /// ViewModel representing a single dairy category line item within a customer's runsheet row.
    /// </summary>
    public class DailyMilkCategoryItemViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ProductType { get; set; } = "Liquid";
        public string UnitOfMeasure { get; set; } = "Pouch";
        public string PricingBasis { get; set; } = "Per Litre";

        /// <summary>
        /// Standard default quantity configured in the customer's subscription.
        /// </summary>
        public decimal DefaultQuantity { get; set; }

        /// <summary>
        /// Actual packaging quantity delivered or entered by the delivery boy.
        /// </summary>
        public decimal DeliveredQuantity { get; set; }

        /// <summary>
        /// Effective unit price applied. Resolved deterministically from CustomerCustomPricing,
        /// or falling back to MilkCategories.DefaultPrice.
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// True if UnitPrice is an agreed custom price override; false if standard default rate.
        /// </summary>
        public bool IsCustomPrice { get; set; }

        /// <summary>
        /// Multiplier to convert packaging units to billing units.
        /// </summary>
        public decimal BillingUnitEquivalent { get; set; } = 1.000m;

        /// <summary>
        /// Multiplier to convert packaging units to liquid litres (0.000 for Non-Liquid products).
        /// </summary>
        public decimal LitreEquivalent { get; set; } = 1.000m;

        /// <summary>
        /// Converted liquid litres = DeliveredQuantity * LitreEquivalent.
        /// </summary>
        public decimal LiquidVolumeLitres { get; set; }

        /// <summary>
        /// Line subtotal = DeliveredQuantity * BillingUnitEquivalent * UnitPrice.
        /// </summary>
        public decimal SubTotal { get; set; }
    }
}
