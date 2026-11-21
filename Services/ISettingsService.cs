using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Settings;

namespace Dairy_Management_System.Services
{
    public interface ISettingsService
    {
        Task<SystemSettingsViewModel> GetSettingsAsync(CancellationToken ct = default);
        Task<bool> UpdateSettingsAsync(SystemSettingsViewModel model, int adminUserId, CancellationToken ct = default);
        Task<string?> GetSettingValueAsync(string key, CancellationToken ct = default);
    }
}
