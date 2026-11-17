namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Lightweight projection model for rendering customer listings, search results,
    /// and filter grids with high performance.
    /// </summary>
    public class CustomerListItemViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? FullNameGujarati { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Area { get; set; }
        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public int? CurrentDeliveryBoyId { get; set; }
        public string? DeliveryBoyName { get; set; }
        public int DeliverySequence { get; set; }
        public string PreferredShift { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsOnVacationToday { get; set; }
        public bool IsActive { get; set; }
        public decimal ActiveSubscriptionsDailyLitres { get; set; }
    }
}
