using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Dashboard;

namespace Dairy_Management_System.Services
{
    public interface IDashboardService
    {
        Task<AdminDashboardViewModel> GetAdminDashboardViewModelAsync(CancellationToken ct = default);
        Task<DeliveryBoyDashboardViewModel> GetDeliveryBoyDashboardViewModelAsync(int userId, CancellationToken ct = default);
    }
}
