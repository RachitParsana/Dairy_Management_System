using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Filtering and pagination parameters for browsing payment receipts.
    /// </summary>
    public class PaymentFilterCriteria
    {
        public int? CustomerId { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? PaymentMode { get; set; } // Online, Offline
        public bool? IsVoided { get; set; }
        public int Offset { get; set; } = 0;
        public int Limit { get; set; } = 25;
    }
}
