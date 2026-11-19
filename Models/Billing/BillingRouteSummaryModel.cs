namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Route-level summary metrics for monthly billing reports.
    /// </summary>
    public class BillingRouteSummaryModel
    {
        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public int TotalCustomers { get; set; }
        public decimal TotalBilledAmount { get; set; }
        public decimal TotalCollectedAmount { get; set; }
        public decimal TotalOutstandingAmount { get; set; }
    }
}
