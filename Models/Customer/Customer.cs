using System;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Represents the core Customer master domain entity corresponding to dbo.Customers.
    /// Pure ADO.NET and relational-database compatible.
    /// </summary>
    public class Customer
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? FullNameGujarati { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? AlternatePhone { get; set; }
        public string Address { get; set; } = string.Empty;
        public string? Area { get; set; }
        public string? Landmark { get; set; }
        public int RouteId { get; set; }
        public int? CurrentDeliveryBoyId { get; set; }
        public int DeliverySequence { get; set; }
        public string PreferredShift { get; set; } = "Both"; // Morning, Evening, Both
        public string Status { get; set; } = "Active"; // Active, Suspended, Inactive
        public string? Notes { get; set; }
        public DateOnly DeliveryStartDate { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
