using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// View and input model for customer vacation requests and approval management.
    /// Captures absence dates, shifts, early resumption, and audit details.
    /// </summary>
    public class CustomerVacationViewModel
    {
        public int VacationId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? ReportedByDeliveryBoyId { get; set; }
        public string? ReportedByName { get; set; }

        [Required(ErrorMessage = "Vacation start date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "Vacation end date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateOnly EndDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Early Resumption Date")]
        public DateOnly? ResumedDate { get; set; }

        [Required]
        [Display(Name = "Applicable Shift")]
        public string Shift { get; set; } = "Both"; // Morning, Evening, Both

        [StringLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
        [Display(Name = "Vacation Reason")]
        public string? Reason { get; set; }

        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Cancelled
        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
