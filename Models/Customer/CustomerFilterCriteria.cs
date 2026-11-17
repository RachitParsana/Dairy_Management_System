namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Search and filter criteria for customer directory queries.
    /// Supports pagination, text search, route and delivery boy filtering.
    /// </summary>
    public class CustomerFilterCriteria
    {
        public string? SearchTerm { get; set; }
        public int? RouteId { get; set; }
        public int? DeliveryBoyId { get; set; }
        public string? Status { get; set; } // Active, Inactive, Suspended, All
        public string? Shift { get; set; } // Morning, Evening, Both
        public bool? IsOnVacation { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}
