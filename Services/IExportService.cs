using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dairy_Management_System.Services
{
    public interface IExportService
    {
        Task<byte[]> GenerateCustomersCsvAsync(int? routeId, bool? isActive, CancellationToken ct = default);
        Task<byte[]> GenerateDailyDeliveriesCsvAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default);
        Task<byte[]> GenerateMonthlyBillsCsvAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default);
        Task<byte[]> GeneratePaymentsCsvAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default);
        Task<byte[]> GenerateCompensationCsvAsync(byte month, short year, CancellationToken ct = default);
    }
}
