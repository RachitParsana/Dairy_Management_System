using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Dashboard;

namespace Dairy_Management_System.Repositories
{
    public interface IDashboardRepository
    {
        Task<AdminDashboardViewModel> GetAdminMetricsAsync(CancellationToken ct = default);
        Task<DeliveryBoyDashboardViewModel> GetDeliveryBoyMetricsAsync(int deliveryBoyId, CancellationToken ct = default);
    }
}
