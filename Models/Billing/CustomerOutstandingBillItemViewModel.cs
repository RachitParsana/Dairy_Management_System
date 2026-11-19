using System;

namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Represents an active published invoice with an unpaid balance, presented to the admin
    /// during payment recording or advance credit allocation.
    /// </summary>
    public class CustomerOutstandingBillItemViewModel
    {
        public long BillId { get; set; }
        public string BillNumber { get; set; } = string.Empty;
        public byte BillingMonth { get; set; }
        public short BillingYear { get; set; }
        public DateOnly PeriodStartDate { get; set; }
        public DateOnly PeriodEndDate { get; set; }
        public decimal CurrentMonthInvoiceAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceAmount { get; set; }
        public DateOnly? PaymentDueDate { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
    }
}
