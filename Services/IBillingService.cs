using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Billing;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract orchestrating business validations, procedural execution, and ledger operations
    /// for Day 5 Billing & Payments Management.
    /// Guarantees strictly append-only financial history, single-invoice allocation, LIFO downward revisions,
    /// and two-tiered payment void idempotency.
    /// </summary>
    public interface IBillingService
    {
        // Invoice Operations
        Task<long> GenerateCustomerBillAsync(int customerId, int month, int year, int adminUserId, CancellationToken cancellationToken = default);
        Task<MonthlyBill?> GetBillDetailsAsync(long billId, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<MonthlyBill> Bills, int TotalCount)> GetBillsContinuousAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<bool> PublishBillAsync(long billId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<int> BulkPublishBillsAsync(int month, int year, int? routeId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> ReviseBillAsync(ReviseBillViewModel model, int adminUserId, CancellationToken cancellationToken = default);

        // Bulk Billing
        Task<IReadOnlyList<CustomerBillingEligibilityModel>> GetEligibleCustomersForBillingAsync(int month, int year, int? routeId, CancellationToken cancellationToken = default);
        Task<BulkBillingResultModel> GenerateBulkMonthlyBillsAsync(int month, int year, int? routeId, int adminUserId, CancellationToken cancellationToken = default);

        // Payments & Ledger
        Task<long> RecordAndAllocateSinglePaymentAsync(RecordSinglePaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> AllocateAdvanceCreditSingleAsync(AllocateAdvanceCreditViewModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<bool> VoidPaymentAsync(VoidPaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<Payment?> GetPaymentDetailsAsync(long paymentId, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<Payment> Payments, int TotalCount)> GetPaymentsHistoryAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CustomerOutstandingBillItemViewModel>> GetCustomerOutstandingBillsAsync(int customerId, CancellationToken cancellationToken = default);
        Task<CustomerBalanceSummaryModel> GetCustomerBalanceSummaryAsync(int customerId, CancellationToken cancellationToken = default);

        // Reports
        Task<BillingSummaryReportModel> GetMonthlyBillingSummaryReportAsync(int month, int year, CancellationToken cancellationToken = default);
    }
}
