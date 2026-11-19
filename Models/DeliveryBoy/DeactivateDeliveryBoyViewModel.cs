using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Dairy_Management_System.Models.Customer;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class DeactivateDeliveryBoyViewModel
    {
        public int DeliveryBoyId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public int? ReassignToDeliveryBoyId { get; set; }

        [Required(ErrorMessage = "Deactivation reason is required.")]
        [MinLength(5, ErrorMessage = "Reason must be at least 5 characters.")]
        [StringLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
        public string Reason { get; set; } = string.Empty;

        public int ActiveCustomerCount { get; set; }
        public IReadOnlyList<DeliveryBoyLookupItem>? AvailableDeliveryBoys { get; set; }
    }
}
