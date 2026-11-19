namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Evaluates customer eligibility for bulk monthly billing generation.
    /// Excludes non-active or non-delivering accounts.
    /// </summary>
    public class CustomerBillingEligibilityModel
    {
        public int CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public int? RouteId { get; set; }
        public string? RouteName { get; set; }
        public bool IsActive { get; set; }
        public int CompletedDeliveriesCount { get; set; }
        public decimal DeliveredLitres { get; set; }
        public bool HasExistingBill { get; set; }
        public long? ExistingBillId { get; set; }
        public string? ExistingBillStatus { get; set; }
        public bool IsEligible => IsActive && CompletedDeliveriesCount > 0 && !HasExistingBill;
    }
}
