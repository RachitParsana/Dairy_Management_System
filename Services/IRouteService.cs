using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Route;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract orchestrating Route master queries and dropdown lookups.
    /// </summary>
    public interface IRouteService
    {
        /// <summary>
        /// Retrieves all active routes from dbo.Routes ordered by RouteName ascending.
        /// Guaranteed to include active routes with zero active customers.
        /// </summary>
        Task<IReadOnlyList<RouteLookupModel>> GetActiveRoutesLookupAsync(CancellationToken cancellationToken = default);
    }
}
