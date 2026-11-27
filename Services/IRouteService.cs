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

    /// <summary>
    /// Extended service contract for Admin Route CRUD and activation management.
    /// </summary>
    public interface IRouteManagementService
    {
        Task<IReadOnlyList<RouteManagementItemModel>> GetAllRoutesAsync(CancellationToken cancellationToken = default);
        Task<RouteOperationResult> CreateRouteAsync(RouteFormModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<RouteOperationResult> UpdateRouteAsync(RouteFormModel model, int adminUserId, CancellationToken cancellationToken = default);
        Task<RouteOperationResult> SetRouteActiveStatusAsync(int routeId, bool isActive, int adminUserId, CancellationToken cancellationToken = default);
    }
}
