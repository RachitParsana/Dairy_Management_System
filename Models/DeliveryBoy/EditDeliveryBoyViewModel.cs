using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class EditDeliveryBoyViewModel
    {
        public int DeliveryBoyId { get; set; }

        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [Phone(ErrorMessage = "Invalid phone format.")]
        [StringLength(15, ErrorMessage = "Phone cannot exceed 15 characters.")]
        public string Phone { get; set; } = string.Empty;

        [StringLength(30, ErrorMessage = "Vehicle number cannot exceed 30 characters.")]
        public string? VehicleNumber { get; set; }

        [Required(ErrorMessage = "Primary route is required.")]
        public int PrimaryRouteId { get; set; }

        public string CompensationType { get; set; } = "Commission";

        [Range(0, 1000000, ErrorMessage = "Monthly base salary must be non-negative.")]
        public decimal MonthlyBaseSalary { get; set; }

        public decimal CurrentCommissionRate { get; set; }

        public IReadOnlyList<RouteLookupModel>? AvailableRoutes { get; set; }
    }

    public class EditDeliveryBoyDto
    {
        public int DeliveryBoyId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? VehicleNumber { get; set; }
        public int PrimaryRouteId { get; set; }
        public decimal? MonthlyBaseSalary { get; set; }
        public int AdminUserId { get; set; }
    }
}
