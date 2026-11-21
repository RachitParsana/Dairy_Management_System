using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dairy_Management_System.Repositories
{
    public interface ISettingsRepository
    {
        Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);
        Task<bool> UpdateAsync(string key, string value, int adminUserId, CancellationToken ct = default);
    }
}
