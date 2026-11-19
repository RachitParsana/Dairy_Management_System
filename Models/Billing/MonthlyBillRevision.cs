using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Immutable historical revision record capturing before and after financial state
    /// of an invoice revision.
    /// </summary>
    public class MonthlyBillRevision
    {
        public long RevisionId { get; set; }
        public long BillId { get; set; }
        public int VersionNumber { get; set; }
        public decimal PriorMilkAmount { get; set; }
        public decimal NewMilkAmount { get; set; }
        public decimal PriorAdjustments { get; set; }
        public decimal NewAdjustments { get; set; }
        public decimal PriorInvoiceAmount { get; set; }
        public decimal NewInvoiceAmount { get; set; }
        public decimal PreviousBalanceSnapshot { get; set; }
        public decimal PaidAmountSnapshot { get; set; }
        public decimal PriorBalanceAmount { get; set; }
        public decimal NewBalanceAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int RevisedBy { get; set; }
        public string? RevisedByUserName { get; set; }
        public DateTime RevisedAt { get; set; }
    }
}
