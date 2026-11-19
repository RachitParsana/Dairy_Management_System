namespace Dairy_Management_System.Models.Billing
{
    /// <summary>
    /// Authoritative account balance summary for a customer.
    /// TotalOutstandingDues: Sum of unpaid balances across published bills (receivables).
    /// TotalAvailableAdvanceCredit: Sum of unallocated funds across active payment receipts.
    /// NetAccountBalance: TotalOutstandingDues - TotalAvailableAdvanceCredit.
    /// Advance credit is never deducted automatically from individual bills.
    /// </summary>
    public class CustomerBalanceSummaryModel
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public decimal TotalOutstandingDues { get; set; }
        public decimal TotalAvailableAdvanceCredit { get; set; }
        public decimal NetAccountBalance => TotalOutstandingDues - TotalAvailableAdvanceCredit;
    }
}
