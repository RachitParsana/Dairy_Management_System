namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Filtering and pagination parameters for browsing monthly bills.
    /// </summary>
    public class MonthlyBillFilterCriteria
    {
        public byte? BillingMonth { get; set; }
        public short? BillingYear { get; set; }
        public int? RouteId { get; set; }
        public string? PaymentStatus { get; set; } // Paid, Partially Paid, Unpaid, Overdue
        public string? SearchText { get; set; }
        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = 25;
    }
}
