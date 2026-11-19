using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Repository contract for read-only Route lookup operations against dbo.Routes.
    /// Returns every active route regardless of customer assignments.
    /// </summary>
    public interface IRouteRepository
    {
        /// <summary>
        /// Retrieves all active routes from dbo.Routes ordered by RouteName ascending.
        /// </summary>
        Task<IReadOnlyList<RouteLookupModel>> GetActiveRoutesLookupAsync(CancellationToken cancellationToken = default);
    }
}
