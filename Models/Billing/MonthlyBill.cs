using System;
using System.Collections.Generic;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Authoritative monthly invoice entity.
    /// Invariant: BalanceAmount = CurrentMonthInvoiceAmount - PaidAmount (receivable debt for this month only).
    /// PreviousBalanceSnapshot is informational only and never added to receivable BalanceAmount.
    /// </summary>
    public class MonthlyBill
    {
        public long BillId { get; set; }
        public string BillNumber { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? RouteName { get; set; }
        public byte BillingMonth { get; set; }
        public short BillingYear { get; set; }
        public DateOnly PeriodStartDate { get; set; }
        public DateOnly PeriodEndDate { get; set; }
        public decimal TotalLiquidMilkVolumeLitres { get; set; }
        public decimal CurrentMonthMilkAmount { get; set; }
        public decimal Adjustments { get; set; }
        public decimal CurrentMonthInvoiceAmount { get; set; }
        public decimal PreviousBalanceSnapshot { get; set; }
        public decimal TotalStatementAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceAmount { get; set; }
        public string PaymentStatus { get; set; } = "Unpaid"; // Paid, Partially Paid, Unpaid, Overdue
        public bool IsPublished { get; set; }
        public DateTime? PublishedAt { get; set; }
        public int Version { get; set; } = 1;
        public string? RevisionReason { get; set; }
        public DateOnly? PaymentDueDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }

        // Navigation Collections
        public List<MonthlyBillItem> Items { get; set; } = new();
        public List<MonthlyBillRevision> Revisions { get; set; } = new();
        public List<PaymentAllocationEvent> AllocationEvents { get; set; } = new();
    }
}
