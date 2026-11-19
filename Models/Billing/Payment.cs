using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Represents a customer payment receipt.
    /// Strictly supports payment modes "Online" and "Offline".
    /// Unallocated credit is preserved on the receipt until manually allocated.
    /// Voided payments hold exactly ₹0.00 advance credit.
    /// </summary>
    public class Payment
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public DateOnly PaymentDate { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = "Offline"; // Online or Offline
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
        public bool IsVoided { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public int ReceivedBy { get; set; }
        public string? ReceivedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }

        // Authoritative ledger metrics calculated from active allocation events
        public decimal AllocatedAmount { get; set; }
        public decimal UnallocatedCredit => IsVoided ? 0.00m : Math.Max(0.00m, Amount - AllocatedAmount);
    }
}
