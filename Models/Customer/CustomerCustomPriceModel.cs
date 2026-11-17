using System;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Represents an individual unit price override agreement for a customer and product category.
    /// Overrides MilkCategories.DefaultPrice for the effective date interval.
    /// </summary>
    public class CustomerCustomPriceModel
    {
        public int PricingId { get; set; }
        public int CustomerId { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public decimal StandardDefaultPrice { get; set; }
        public decimal CustomPrice { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
