using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class CreateDeliveryBoyViewModel
    {
        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [Phone(ErrorMessage = "Invalid phone format.")]
        [StringLength(15, ErrorMessage = "Phone cannot exceed 15 characters.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [StringLength(30, ErrorMessage = "Vehicle number cannot exceed 30 characters.")]
        public string? VehicleNumber { get; set; }

        [Required(ErrorMessage = "Primary route is required.")]
        public int PrimaryRouteId { get; set; }

        [Required(ErrorMessage = "Compensation type is required.")]
        public string CompensationType { get; set; } = "Commission"; // 'Fixed Salary' or 'Commission'

        [Range(0, 1000000, ErrorMessage = "Monthly base salary must be non-negative.")]
        public decimal MonthlyBaseSalary { get; set; } = 0.00m;

        [Range(0, 100, ErrorMessage = "Initial commission rate must be non-negative.")]
        public decimal InitialCommissionRate { get; set; } = 0.00m;

        [DataType(DataType.Date)]
        public DateOnly? EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        public string? Notes { get; set; }

        public IReadOnlyList<RouteLookupModel>? AvailableRoutes { get; set; }
    }

    public class CreateDeliveryBoyDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string? VehicleNumber { get; set; }
        public int PrimaryRouteId { get; set; }
        public string CompensationType { get; set; } = "Commission";
        public decimal MonthlyBaseSalary { get; set; }
        public decimal InitialCommissionRate { get; set; }
        public DateOnly? EffectiveFrom { get; set; }
        public string? Notes { get; set; }
        public int AdminUserId { get; set; }
    }
}
