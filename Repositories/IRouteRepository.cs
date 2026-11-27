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

    /// <summary>
    /// Extended repository contract for Admin Route CRUD and lifecycle management.
    /// </summary>
    public interface IRouteManagementRepository
    {
        Task<IReadOnlyList<RouteManagementItemModel>> GetAllRoutesAsync(CancellationToken cancellationToken = default);
        Task<RouteOperationResult> CreateRouteAsync(string routeCode, string routeName, string? description, int adminUserId, CancellationToken cancellationToken = default);
        Task<RouteOperationResult> UpdateRouteAsync(int routeId, string routeCode, string routeName, string? description, int adminUserId, CancellationToken cancellationToken = default);
        Task<RouteOperationResult> SetRouteActiveStatusAsync(int routeId, bool isActive, int adminUserId, CancellationToken cancellationToken = default);
    }
}
