using System.Collections.Generic;

namespace Dairy_Management_System.Models.DeliveryBoy
{
    public class DeliveryBoyCustomerRosterItemViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string Landmark { get; set; } = string.Empty;
        public int DeliverySequence { get; set; }
        public int? SubscriptionId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal DailyQuantity { get; set; }
        public string ShiftPreference { get; set; } = string.Empty;
        public bool IsOnVacationToday { get; set; }
    }
}
