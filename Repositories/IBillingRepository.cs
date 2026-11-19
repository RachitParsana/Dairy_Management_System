using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Billing;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Authoritative data access contract for Day 5 Billing & Payments Management.
    /// Operates exclusively through compiled static SQL stored procedures and queries
    /// the append-only dbo.PaymentAllocationEvents ledger. Never accesses dbo.PaymentAllocations.
    /// </summary>
    public interface IBillingRepository
    {
        // Invoice Operations
        Task<long> GenerateCustomerBillAsync(int customerId, int month, int year, int adminUserId, CancellationToken cancellationToken = default);
        Task<MonthlyBill?> GetBillByIdAsync(long billId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MonthlyBill>> GetBillsAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<int> GetBillsCountAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<bool> PublishBillAsync(long billId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<int> BulkPublishBillsAsync(int month, int year, int? routeId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> ReviseBillAsync(ReviseBillViewModel model, int adminUserId, CancellationToken cancellationToken = default);

        // Bulk Operations & Eligibility
        Task<IReadOnlyList<CustomerBillingEligibilityModel>> GetEligibleCustomersForBillingAsync(int month, int year, int? routeId, CancellationToken cancellationToken = default);
        Task<BulkBillingResultModel> GenerateBulkMonthlyBillsAsync(int month, int year, int? routeId, int adminUserId, CancellationToken cancellationToken = default);

        // Append-Only Payment Operations (100% Authoritative on PaymentAllocationEvents via Stored Procs)
        Task<long> RecordAndAllocateSinglePaymentAsync(RecordSinglePaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> AllocateAdvanceCreditSingleAsync(long paymentId, long targetBillId, decimal amount, string? reason, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> VoidPaymentAsync(long paymentId, string voidReason, int adminUserId, CancellationToken cancellationToken = default);
        Task<Payment?> GetPaymentByIdAsync(long paymentId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Payment>> GetPaymentsAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<int> GetPaymentsCountAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PaymentAllocationEvent>> GetAllocationEventsByBillIdAsync(long billId, CancellationToken cancellationToken = default);

        // Customer Ledger & Balances (Authoritative Model)
        Task<IReadOnlyList<CustomerOutstandingBillItemViewModel>> GetCustomerOutstandingBillsAsync(int customerId, CancellationToken cancellationToken = default);
        Task<CustomerBalanceSummaryModel> GetCustomerBalanceSummaryAsync(int customerId, CancellationToken cancellationToken = default);

        // Reports
        Task<BillingSummaryReportModel> GetMonthlyBillingSummaryReportAsync(int month, int year, CancellationToken cancellationToken = default);
    }
}
