using System;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents an itemized category entry for a customer's daily delivery.
    /// Maps to database table dbo.DailyMilkDeliveryItems.
    /// Preserves immutable historical snapshots of packaging, volume conversion, pricing basis, and unit rate.
    /// </summary>
    public class DailyMilkCategoryEntry
    {
        /// <summary>
        /// Surrogate primary key.
        /// </summary>
        public long DeliveryItemId { get; set; }

        /// <summary>
        /// Foreign key referencing the parent delivery header in dbo.DailyMilkDeliveries.
        /// </summary>
        public long DeliveryId { get; set; }

        /// <summary>
        /// Foreign key referencing the dairy category in dbo.MilkCategories.
        /// </summary>
        public int CategoryId { get; set; }

        /// <summary>
        /// Optional navigation property / display name for the category.
        /// </summary>
        public string? CategoryName { get; set; }

        /// <summary>
        /// Physical packaging units delivered (e.g., number of pouches, bottles, packets, or tubs).
        /// Strictly 0.000 for non-delivered or vacation entries.
        /// </summary>
        public decimal PackagingQuantity { get; set; }

        /// <summary>
        /// Historical snapshot of product classification captured at delivery entry time ('Liquid' or 'Non-Liquid').
        /// </summary>
        public string ProductTypeSnapshot { get; set; } = "Liquid";

        /// <summary>
        /// Historical snapshot of physical unit of measure captured at delivery entry time ('Pouch', 'Bottle', 'Litre', 'Kg', 'Pack', 'Tub').
        /// </summary>
        public string UnitOfMeasureSnapshot { get; set; } = "Pouch";

        /// <summary>
        /// Historical snapshot of pricing basis captured at delivery entry time ('Per Litre', 'Per Kg', 'Per Unit').
        /// </summary>
        public string PricingBasisSnapshot { get; set; } = "Per Litre";

        /// <summary>
        /// Historical conversion multiplier from packaging units to billing units.
        /// For example, a 500ml pouch with 'Per Litre' pricing has BillingUnitEquivalent = 0.500.
        /// </summary>
        public decimal BillingUnitEquivalentSnapshot { get; set; } = 1.000m;

        /// <summary>
        /// Historical conversion multiplier from packaging units to liquid litres.
        /// Strictly 0.000 for Non-Liquid products (e.g., Paneer, Curd, Ghee).
        /// </summary>
        public decimal LitreEquivalentSnapshot { get; set; } = 1.000m;

        /// <summary>
        /// Converted liquid milk volume in litres = PackagingQuantity * LitreEquivalentSnapshot.
        /// Strictly 0.000 for Non-Liquid products or when Status is 'Not Delivered'.
        /// </summary>
        public decimal LiquidVolumeLitres { get; set; }

        /// <summary>
        /// Agreed unit price snapshot resolved at delivery entry time from CustomerCustomPricing or MilkCategories.DefaultPrice.
        /// </summary>
        public decimal UnitPriceSnapshot { get; set; }

        /// <summary>
        /// Total monetary charge for this category line = PackagingQuantity * BillingUnitEquivalentSnapshot * UnitPriceSnapshot.
        /// Strictly 0.00 for non-delivered or vacation entries.
        /// </summary>
        public decimal SubTotal { get; set; }
    }
}
