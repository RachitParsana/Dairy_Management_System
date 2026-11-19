using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Compensation;

namespace Dairy_Management_System.Repositories
{
    public interface ICompensationRepository
    {
        Task<IReadOnlyList<MonthlyCompensationListItemDto>> GetCompensationsAsync(int month, int year, string? status, string? compensationType, CancellationToken ct = default);
        Task<MonthlyCompensationDetailsDto?> GetCompensationDetailsAsync(long compensationId, CancellationToken ct = default);
        Task<long> CalculateOrUpdateDraftAsync(int deliveryBoyId, int month, int year, int adminUserId, CancellationToken ct = default);
        Task<int> BulkCalculateDraftsAsync(int month, int year, int adminUserId, CancellationToken ct = default);
        Task<bool> ApplyAdjustmentAsync(long compensationId, string adjustmentType, decimal adjustmentAmount, string reason, int adminUserId, CancellationToken ct = default);
        Task<IReadOnlyList<CompensationAdjustmentDto>> GetAdjustmentsAsync(long compensationId, CancellationToken ct = default);
        Task<bool> FinalizeCompensationAsync(long compensationId, int adminUserId, CancellationToken ct = default);
        Task<bool> RecordPayoutAsync(long compensationId, DateTime paidAt, string paymentReference, string? notes, int adminUserId, CancellationToken ct = default);
    }
}
