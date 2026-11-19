using System.Collections.Generic;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Aggregate financial reporting model for monthly billing and collections.
    /// </summary>
    public class BillingSummaryReportModel
    {
        public byte BillingMonth { get; set; }
        public short BillingYear { get; set; }
        public decimal TotalVolumeLitres { get; set; }
        public decimal TotalBilledAmount { get; set; }
        public decimal TotalAdjustments { get; set; }
        public decimal TotalInvoiceAmount { get; set; }
        public decimal TotalCollections { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int TotalBillsCount { get; set; }
        public int PaidBillsCount { get; set; }
        public int PartiallyPaidBillsCount { get; set; }
        public int UnpaidBillsCount { get; set; }
        public int OverdueBillsCount { get; set; }
        public List<BillingRouteSummaryModel> RouteBreakdowns { get; set; } = new();
    }
}
