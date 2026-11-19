using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Route;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Pure ADO.NET repository retrieving active routes from dbo.Routes.
    /// Executes under least-privilege db_dms_app role via compiled stored procedure.
    /// </summary>
    public class RouteRepository : IRouteRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<RouteRepository> _logger;

        public RouteRepository(ISqlConnectionFactory connectionFactory, ILogger<RouteRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<RouteLookupModel>> GetActiveRoutesLookupAsync(CancellationToken cancellationToken = default)
        {
            var routes = new List<RouteLookupModel>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Routes_GetActiveLookup", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    routes.Add(new RouteLookupModel
                    {
                        RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                        RouteCode = reader.GetString(reader.GetOrdinal("RouteCode")),
                        RouteName = reader.GetString(reader.GetOrdinal("RouteName"))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve active routes lookup from database.");
                throw;
            }

            return routes;
        }
    }
}
