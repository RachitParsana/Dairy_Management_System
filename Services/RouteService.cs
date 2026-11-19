using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Route;
using Dairy_Management_System.Repositories;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Application service orchestrating Route master lookups.
    /// </summary>
    public class RouteService : IRouteService
    {
        private readonly IRouteRepository _routeRepository;
        private readonly ILogger<RouteService> _logger;

        public RouteService(IRouteRepository routeRepository, ILogger<RouteService> logger)
        {
            _routeRepository = routeRepository ?? throw new ArgumentNullException(nameof(routeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<RouteLookupModel>> GetActiveRoutesLookupAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving active routes lookup via RouteRepository.");
            return await _routeRepository.GetActiveRoutesLookupAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
