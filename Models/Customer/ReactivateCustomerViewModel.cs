using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Input ViewModel for Customer Reactivation workflow.
    /// Resumes service starting from ReactivationDate by establishing a new delivery boy assignment,
    /// route sequence, and fresh milk subscription lines.
    /// </summary>
    public class ReactivateCustomerViewModel
    {
        [Required]
        public int CustomerId { get; set; }

        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Service resumption date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Service Resumption Date")]
        public DateOnly ReactivationDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required(ErrorMessage = "Please select a delivery route.")]
        [Display(Name = "Delivery Route")]
        public int RouteId { get; set; }

        [Required(ErrorMessage = "Delivery sequence number is required.")]
        [Range(0, 9999, ErrorMessage = "Sequence must be between 0 and 9999.")]
        [Display(Name = "Delivery Sequence")]
        public int DeliverySequence { get; set; }

        [Required(ErrorMessage = "Please assign a delivery boy.")]
        [Display(Name = "Assigned Delivery Boy")]
        public int DeliveryBoyId { get; set; }

        [StringLength(250, ErrorMessage = "Notes cannot exceed 250 characters.")]
        [Display(Name = "Reactivation Notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Fresh milk subscription lines activated upon service resumption.
        /// </summary>
        public List<SubscriptionLineInputModel> Subscriptions { get; set; } = new();
    }
}
