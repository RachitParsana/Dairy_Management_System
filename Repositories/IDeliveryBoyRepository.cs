using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DeliveryBoy;

namespace Dairy_Management_System.Repositories
{
    public interface IDeliveryBoyRepository
    {
        Task<int> CreateDeliveryBoyAsync(CreateDeliveryBoyDto dto, CancellationToken ct = default);
        Task<DeliveryBoyDetailsDto?> GetByIdAsync(int deliveryBoyId, CancellationToken ct = default);
        Task<IReadOnlyList<DeliveryBoyListItemDto>> GetDirectoryAsync(DeliveryBoyFilterCriteria criteria, CancellationToken ct = default);
        Task<bool> UpdateProfileAsync(EditDeliveryBoyDto dto, CancellationToken ct = default);
        Task<bool> DeactivateAsync(int deliveryBoyId, int? reassignToDeliveryBoyId, string reason, int adminUserId, CancellationToken ct = default);
        Task<bool> ReactivateAsync(int deliveryBoyId, decimal? newCommissionRate, int adminUserId, CancellationToken ct = default);
        Task<IReadOnlyList<DeliveryBoyLookupItem>> GetActiveLookupAsync(CancellationToken ct = default);
    }
}
