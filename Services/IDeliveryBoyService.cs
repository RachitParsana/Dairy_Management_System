using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DeliveryBoy;

namespace Dairy_Management_System.Services
{
    public interface IDeliveryBoyService
    {
        Task<int> CreateDeliveryBoyAsync(CreateDeliveryBoyViewModel model, int adminUserId, CancellationToken ct = default);
        Task<DeliveryBoyDetailsDto?> GetDetailsAsync(int deliveryBoyId, CancellationToken ct = default);
        Task<IReadOnlyList<DeliveryBoyListItemDto>> GetDirectoryAsync(DeliveryBoyFilterCriteria criteria, CancellationToken ct = default);
        Task<EditDeliveryBoyViewModel?> GetForEditAsync(int deliveryBoyId, CancellationToken ct = default);
        Task<bool> UpdateProfileAsync(EditDeliveryBoyViewModel model, int adminUserId, CancellationToken ct = default);
        Task<bool> DeactivateAsync(int deliveryBoyId, int? reassignToDeliveryBoyId, string reason, int adminUserId, CancellationToken ct = default);
        Task<bool> ReactivateAsync(int deliveryBoyId, decimal? newCommissionRate, int adminUserId, CancellationToken ct = default);
        Task<IReadOnlyList<DeliveryBoyLookupItem>> GetActiveLookupAsync(CancellationToken ct = default);
    }
}
