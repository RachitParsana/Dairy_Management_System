using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Immutable audit log for payment lifecycle events (creation, voiding, allocation).
    /// </summary>
    public class PaymentAuditLog
    {
        public long AuditLogId { get; set; }
        public long PaymentId { get; set; }
        public string Action { get; set; } = string.Empty;
        public decimal? OldAmount { get; set; }
        public decimal? NewAmount { get; set; }
        public string? OldStatus { get; set; }
        public string? NewStatus { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int PerformedBy { get; set; }
        public string? PerformedByUserName { get; set; }
        public DateTime PerformedAt { get; set; }
    }
}
