using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Customer
{
    /// <summary>
    /// Input view model for customer onboarding.
    /// Captures basic details, delivery logistics, and initial multi-category subscriptions.
    /// </summary>
    public class CreateCustomerViewModel
    {
        [Required(ErrorMessage = "Full customer name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Customer name must be between 2 and 100 characters.")]
        [Display(Name = "Full Name (English)")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Gujarati name cannot exceed 100 characters.")]
        [Display(Name = "Full Name (Gujarati)")]
        public string? FullNameGujarati { get; set; }

        [Required(ErrorMessage = "Primary mobile number is required.")]
        [RegularExpression(@"^\+?[0-9]{10,15}$", ErrorMessage = "Enter a valid 10 to 15 digit mobile number.")]
        [Display(Name = "Mobile Number")]
        public string Phone { get; set; } = string.Empty;

        [RegularExpression(@"^\+?[0-9]{10,15}$", ErrorMessage = "Enter a valid 10 to 15 digit alternate number.")]
        [Display(Name = "Alternate Mobile Number")]
        public string? AlternatePhone { get; set; }

        [Required(ErrorMessage = "Delivery address is required.")]
        [StringLength(250, MinimumLength = 5, ErrorMessage = "Address must be between 5 and 250 characters.")]
        [Display(Name = "Address")]
        public string Address { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Area cannot exceed 100 characters.")]
        [Display(Name = "Area / Locality")]
        public string? Area { get; set; }

        [StringLength(100, ErrorMessage = "Landmark cannot exceed 100 characters.")]
        [Display(Name = "Landmark")]
        public string? Landmark { get; set; }

        [Required(ErrorMessage = "Please select a delivery route.")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a valid delivery route.")]
        [Display(Name = "Delivery Route")]
        public int RouteId { get; set; }

        [Display(Name = "Assigned Delivery Boy")]
        public int? CurrentDeliveryBoyId { get; set; }

        [Range(0, 9999, ErrorMessage = "Sequence number must be non-negative.")]
        [Display(Name = "Delivery Sequence")]
        public int DeliverySequence { get; set; } = 0;

        [Required(ErrorMessage = "Preferred shift is required.")]
        [Display(Name = "Preferred Shift")]
        public string PreferredShift { get; set; } = "Both";

        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
        [Display(Name = "Delivery Instructions / Notes")]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Delivery start date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Delivery Start Date")]
        public DateOnly DeliveryStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        /// <summary>
        /// Initial multi-category product subscriptions configured during onboarding.
        /// </summary>
        public List<SubscriptionLineInputModel> Subscriptions { get; set; } = new();
    }

    /// <summary>
    /// Line item input for a category subscription during onboarding.
    /// </summary>
    public class SubscriptionLineInputModel
    {
        [Required]
        public int CategoryId { get; set; }

        [Required]
        public string Shift { get; set; } = "Morning"; // Morning, Evening, Both

        [Range(0.25, 50.0, ErrorMessage = "Quantity must be between 0.25 and 50.")]
        public decimal Quantity { get; set; } = 1.0m;

        public string DeliverySchedule { get; set; } = "Daily";

        public string DeliveryDays { get; set; } = "1,2,3,4,5,6,7";

        public decimal? CustomPriceOverride { get; set; }
    }
}
