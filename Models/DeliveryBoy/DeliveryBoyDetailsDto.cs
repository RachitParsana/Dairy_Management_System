using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class DeliveryBoyDetailsDto
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
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }
        public string? UpdatedByName { get; set; }

        public List<AssignedCustomerItemDto> AssignedCustomers { get; set; } = new();
        public List<CommissionRateTierDto> CommissionRateHistory { get; set; } = new();
        public List<RecentCompensationItemDto> RecentCompensations { get; set; } = new();
    }

    public class AssignedCustomerItemDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? FullNameGujarati { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Area { get; set; }
        public int DeliverySequence { get; set; }
        public string? RouteName { get; set; }
        public DateOnly AssignedFrom { get; set; }
        public DateOnly? AssignedTo { get; set; }
        public bool AssignmentIsActive { get; set; }
    }

    public class CommissionRateTierDto
    {
        public int RateId { get; set; }
        public int DeliveryBoyId { get; set; }
        public decimal CommissionRatePerLitre { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
    }

    public class RecentCompensationItemDto
    {
        public long CompensationId { get; set; }
        public int DeliveryBoyId { get; set; }
        public byte Month { get; set; }
        public short Year { get; set; }
        public string CompensationType { get; set; } = string.Empty;
        public decimal EligibleCompletedLitres { get; set; }
        public decimal ApplicableCommissionRate { get; set; }
        public decimal CalculatedCommission { get; set; }
        public decimal FixedSalaryAmount { get; set; }
        public decimal Adjustments { get; set; }
        public decimal NetPayableAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? FinalizedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? PaymentReference { get; set; }
    }
}
