using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DailyMilk
{
    /// <summary>
    /// ViewModel representing full details of a customer's daily delivery transaction.
    /// Used by Admin inspection screens, audit views, and delivery edit modals.
    /// </summary>
    public class DailyMilkEntryViewModel
    {
        public long DeliveryId { get; set; }
        public long? BatchId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerNameGujarati { get; set; }
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerAddress { get; set; } = string.Empty;

        public int DeliveryBoyId { get; set; }
        public string DeliveryBoyName { get; set; } = string.Empty;
        public string DeliveryBoyPhone { get; set; } = string.Empty;

        public DateOnly EntryDate { get; set; }
        public string Shift { get; set; } = "Morning";

        [Required]
        [RegularExpression("^(Completed|Pending|Not Delivered)$", ErrorMessage = "Status must be 'Completed', 'Pending', or 'Not Delivered'.")]
        public string Status { get; set; } = "Pending";

        public int? VacationId { get; set; }
        public string? VacationReason { get; set; }
        public bool IsOnVacation { get; set; }

        public string? NonDeliveryReason { get; set; }

        public decimal CommissionRateSnapshot { get; set; }
        public decimal TotalLiquidVolumeLitres { get; set; }
        public decimal EligibleLitreQuantity { get; set; }
        public decimal TotalAmount { get; set; }

        public bool IsBatchSubmitted { get; set; }
        public DateTime? BatchSubmittedAt { get; set; }

        public bool IsCorrected { get; set; }
        public DateTime? LastCorrectedAt { get; set; }
        public string? LastCorrectedByName { get; set; }

        public DateTime CreatedAt { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }

        public List<DailyMilkCategoryItemViewModel> Items { get; set; } = new();

        /// <summary>
        /// Historical corrections log for this delivery record.
        /// </summary>
        public List<DailyMilkCorrectionRecord> Corrections { get; set; } = new();
    }
}
