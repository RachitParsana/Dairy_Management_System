using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Represents an itemized category entry within a monthly customer invoice.
    /// Snapshots unit price at billing generation time to maintain deterministic historical pricing.
    /// </summary>
    public class MonthlyBillItem
    {
        public long BillItemId { get; set; }
        public long BillId { get; set; }
        public int MilkCategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int DeliveredDaysCount { get; set; }
        public int QuantityCount { get; set; }
        public decimal LiquidVolumeLitres { get; set; }
        public decimal UnitPriceSnapshot { get; set; }
        public decimal SubTotal { get; set; }
    }
}
