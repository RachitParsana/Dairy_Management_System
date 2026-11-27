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
    public class RouteService : IRouteService, IRouteManagementService
    {
        private readonly IRouteRepository _routeRepository;
        private readonly IRouteManagementRepository? _routeManagementRepository;
        private readonly ILogger<RouteService> _logger;

        public RouteService(IRouteRepository routeRepository, ILogger<RouteService> logger)
        {
            _routeRepository = routeRepository ?? throw new ArgumentNullException(nameof(routeRepository));
            _routeManagementRepository = routeRepository as IRouteManagementRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<RouteLookupModel>> GetActiveRoutesLookupAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving active routes lookup via RouteRepository.");
            return await _routeRepository.GetActiveRoutesLookupAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<RouteManagementItemModel>> GetAllRoutesAsync(CancellationToken cancellationToken = default)
        {
            if (_routeManagementRepository == null)
            {
                throw new InvalidOperationException("IRouteManagementRepository is not implemented by the registered route repository.");
            }
            return await _routeManagementRepository.GetAllRoutesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<RouteOperationResult> CreateRouteAsync(RouteFormModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_routeManagementRepository == null)
            {
                return RouteOperationResult.Failure("Route management repository is unavailable.");
            }
            if (adminUserId <= 0)
            {
                return RouteOperationResult.Failure("Valid administrator identity is required.");
            }

            return await _routeManagementRepository.CreateRouteAsync(
                model.RouteCode,
                model.RouteName,
                model.Description,
                adminUserId,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<RouteOperationResult> UpdateRouteAsync(RouteFormModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_routeManagementRepository == null)
            {
                return RouteOperationResult.Failure("Route management repository is unavailable.");
            }
            if (model.RouteId <= 0)
            {
                return RouteOperationResult.Failure("Invalid route identifier.");
            }
            if (adminUserId <= 0)
            {
                return RouteOperationResult.Failure("Valid administrator identity is required.");
            }

            return await _routeManagementRepository.UpdateRouteAsync(
                model.RouteId,
                model.RouteCode,
                model.RouteName,
                model.Description,
                adminUserId,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<RouteOperationResult> SetRouteActiveStatusAsync(int routeId, bool isActive, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (_routeManagementRepository == null)
            {
                return RouteOperationResult.Failure("Route management repository is unavailable.");
            }
            if (routeId <= 0)
            {
                return RouteOperationResult.Failure("Invalid route identifier.");
            }
            if (adminUserId <= 0)
            {
                return RouteOperationResult.Failure("Valid administrator identity is required.");
            }

            return await _routeManagementRepository.SetRouteActiveStatusAsync(routeId, isActive, adminUserId, cancellationToken).ConfigureAwait(false);
        }
    }
}
