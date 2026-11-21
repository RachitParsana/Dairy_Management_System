namespace Dairy_Management_System.Models.Reports
{
    public class CustomerBalanceAgingDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;

        // Approved month-based ageing buckets computed strictly from unpaid invoice portions (BalanceAmount)
        public decimal UnpaidBalance1Month { get; set; }
        public decimal UnpaidBalance2Months { get; set; }
        public decimal UnpaidBalance3Months { get; set; }
        public decimal UnpaidBalance4To5Months { get; set; }
        public decimal UnpaidBalance6PlusMonths { get; set; }

        public decimal TotalOutstanding { get; set; }
        public int UnpaidBillsCount { get; set; }
    }
}
