using System;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class DeliveryBoyListItemDto
    {
        public int DeliveryBoyId { get; set; }
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? VehicleNumber { get; set; }
        public int? PrimaryRouteId { get; set; }
        public string? RouteName { get; set; }
        public string? RouteCode { get; set; }
        public string CompensationType { get; set; } = "Commission"; // 'Fixed Salary' or 'Commission'
        public decimal CurrentCommissionRate { get; set; }
        public decimal MonthlyBaseSalary { get; set; }
        public int ActiveCustomerCount { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
