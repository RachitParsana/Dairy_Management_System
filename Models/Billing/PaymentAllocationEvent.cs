using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Strictly append-only payment allocation event representing an immutable
    /// transaction in the authoritative payment allocation ledger.
    /// Direction is +1 for allocations and -1 for reversals.
    /// </summary>
    public class PaymentAllocationEvent
    {
        public long AllocationEventId { get; set; }
        public long PaymentId { get; set; }
        public string? ReceiptNumber { get; set; }
        public long BillId { get; set; }
        public string? BillNumber { get; set; }
        public string EventType { get; set; } = "InitialAllocation";
        public long? OriginalAllocationEventId { get; set; }
        public decimal Amount { get; set; }
        public short Direction { get; set; } // +1 or -1
        public decimal SignedAmount => Amount * Direction;
        public string Reason { get; set; } = string.Empty;
        public long? RelatedRevisionId { get; set; }
        public long? LegacyAllocationId { get; set; }
        public int PerformedBy { get; set; }
        public string PerformedByUserName { get; set; } = string.Empty;
        public DateTime PerformedAt { get; set; }
    }
}
