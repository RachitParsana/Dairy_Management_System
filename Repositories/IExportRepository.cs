using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Export;

namespace Dairy_Management_System.Repositories
{
    public interface IExportRepository
    {
        Task<IReadOnlyList<CustomerExportItemDto>> GetCustomersExportAsync(int? routeId, bool? isActive, CancellationToken ct = default);
        Task<IReadOnlyList<DailyDeliveryExportItemDto>> GetDailyDeliveriesExportAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default);
        Task<IReadOnlyList<MonthlyBillExportItemDto>> GetMonthlyBillsExportAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default);
        Task<IReadOnlyList<PaymentExportItemDto>> GetPaymentsExportAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default);
        Task<IReadOnlyList<CompensationExportItemDto>> GetCompensationExportAsync(byte month, short year, CancellationToken ct = default);
    }
}
