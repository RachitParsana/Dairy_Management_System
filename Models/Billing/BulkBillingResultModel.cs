using System.Collections.Generic;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Outcome summary of a bulk monthly billing generation run.
    /// </summary>
    public class BulkBillingResultModel
    {
        public byte BillingMonth { get; set; }
        public short BillingYear { get; set; }
        public int TotalEligible { get; set; }
        public int TotalSucceeded { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalFailed { get; set; }
        public List<string> Messages { get; set; } = new();
    }
}
