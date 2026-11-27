using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// Represents a delivery runsheet batch summary for Administrator lock/unlock management.
    /// </summary>
    public class DailyMilkBatchAdminItemDto
    {
        public long BatchId { get; set; }
        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public string DeliveryBoyPhone { get; set; } = string.Empty;
        public int RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public int TotalAssignedCustomers { get; set; }
        public int CompletedCount { get; set; }
        public int PendingCount { get; set; }
        public int NotDeliveredCount { get; set; }
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? SubmittedByName { get; set; }
        public bool HasPublishedBillLock { get; set; }
        public bool HasFinalizedCompensationLock { get; set; }

        public bool IsLocked => string.Equals(Status, "Submitted", StringComparison.OrdinalIgnoreCase);
        public bool CanUnlock => IsLocked && !HasPublishedBillLock && !HasFinalizedCompensationLock;
    }

    public class UnlockDailyMilkBatchRequest
    {
        [Range(1, long.MaxValue, ErrorMessage = "Invalid batch identifier.")]
        public long BatchId { get; set; }

        [Required(ErrorMessage = "Unlock justification is required.")]
        [MinLength(5, ErrorMessage = "Unlock justification must be at least 5 characters long.")]
        [StringLength(250, ErrorMessage = "Unlock justification cannot exceed 250 characters.")]
        public string UnlockReason { get; set; } = string.Empty;
    }
}
