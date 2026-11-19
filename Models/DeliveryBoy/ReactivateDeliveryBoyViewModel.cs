using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class ReactivateDeliveryBoyViewModel
    {
        public int DeliveryBoyId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string CompensationType { get; set; } = "Commission";

        [Range(0.01, 100, ErrorMessage = "Commission rate must be greater than zero for commission-based employees.")]
        public decimal? NewCommissionRate { get; set; }
    }
}
