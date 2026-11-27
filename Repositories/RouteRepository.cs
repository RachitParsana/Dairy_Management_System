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
    public class RouteRepository : IRouteRepository, IRouteManagementRepository
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

        public async Task<IReadOnlyList<RouteManagementItemModel>> GetAllRoutesAsync(CancellationToken cancellationToken = default)
        {
            var routes = new List<RouteManagementItemModel>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Routes_GetAll", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                int ordRouteId = reader.GetOrdinal("RouteId");
                int ordRouteCode = reader.GetOrdinal("RouteCode");
                int ordRouteName = reader.GetOrdinal("RouteName");
                int ordDescription = reader.GetOrdinal("Description");
                int ordIsActive = reader.GetOrdinal("IsActive");
                int ordCreatedAt = reader.GetOrdinal("CreatedAt");
                int ordUpdatedAt = reader.GetOrdinal("UpdatedAt");
                int ordDbCount = reader.GetOrdinal("ActiveDeliveryBoyCount");
                int ordCustCount = reader.GetOrdinal("ActiveCustomerCount");

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    routes.Add(new RouteManagementItemModel
                    {
                        RouteId = reader.GetInt32(ordRouteId),
                        RouteCode = reader.GetString(ordRouteCode),
                        RouteName = reader.GetString(ordRouteName),
                        Description = reader.IsDBNull(ordDescription) ? null : reader.GetString(ordDescription),
                        IsActive = reader.GetBoolean(ordIsActive),
                        CreatedAt = reader.GetDateTime(ordCreatedAt),
                        UpdatedAt = reader.IsDBNull(ordUpdatedAt) ? null : reader.GetDateTime(ordUpdatedAt),
                        ActiveDeliveryBoyCount = reader.GetInt32(ordDbCount),
                        ActiveCustomerCount = reader.GetInt32(ordCustCount)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve route management catalog.");
                throw;
            }

            return routes;
        }

        public async Task<RouteOperationResult> CreateRouteAsync(string routeCode, string routeName, string? description, int adminUserId, CancellationToken cancellationToken = default)
        {
            string normalizedCode = (routeCode ?? string.Empty).Trim().ToUpperInvariant();
            string normalizedName = (routeName ?? string.Empty).Trim();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Routes_Create", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@RouteCode", SqlDbType.NVarChar, 20) { Value = normalizedCode });
                command.Parameters.Add(new SqlParameter("@RouteName", SqlDbType.NVarChar, 100) { Value = normalizedName });
                command.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, 250)
                {
                    Value = string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim()
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                var newIdParam = new SqlParameter("@NewRouteId", SqlDbType.Int) { Direction = ParameterDirection.Output };
                command.Parameters.Add(newIdParam);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                int newRouteId = newIdParam.Value != DBNull.Value ? Convert.ToInt32(newIdParam.Value) : 0;

                return RouteOperationResult.Success(newRouteId, normalizedCode, normalizedName, $"Route '{normalizedName} ({normalizedCode})' created successfully.");
            }
            catch (SqlException ex) when (ex.Number is >= 50060 and <= 50069 or 2627 or 2601)
            {
                _logger.LogWarning(ex, "Validation failure creating route {RouteCode}", normalizedCode);
                string message = ex.Number is 2627 or 2601
                    ? "A route with this code already exists."
                    : ex.Message;
                return RouteOperationResult.Failure(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating route {RouteCode}", normalizedCode);
                throw;
            }
        }

        public async Task<RouteOperationResult> UpdateRouteAsync(int routeId, string routeCode, string routeName, string? description, int adminUserId, CancellationToken cancellationToken = default)
        {
            string normalizedCode = (routeCode ?? string.Empty).Trim().ToUpperInvariant();
            string normalizedName = (routeName ?? string.Empty).Trim();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Routes_Update", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = routeId });
                command.Parameters.Add(new SqlParameter("@RouteCode", SqlDbType.NVarChar, 20) { Value = normalizedCode });
                command.Parameters.Add(new SqlParameter("@RouteName", SqlDbType.NVarChar, 100) { Value = normalizedName });
                command.Parameters.Add(new SqlParameter("@Description", SqlDbType.NVarChar, 250)
                {
                    Value = string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim()
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return RouteOperationResult.Success(routeId, normalizedCode, normalizedName, $"Route '{normalizedName} ({normalizedCode})' updated successfully.");
            }
            catch (SqlException ex) when (ex.Number is >= 50060 and <= 50069 or 2627 or 2601)
            {
                _logger.LogWarning(ex, "Validation failure updating route {RouteId}", routeId);
                string message = ex.Number is 2627 or 2601
                    ? "Another route already uses this code."
                    : ex.Message;
                return RouteOperationResult.Failure(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating route {RouteId}", routeId);
                throw;
            }
        }

        public async Task<RouteOperationResult> SetRouteActiveStatusAsync(int routeId, bool isActive, int adminUserId, CancellationToken cancellationToken = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Routes_SetActive", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = routeId });
                command.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = isActive });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                string verb = isActive ? "activated" : "deactivated";
                return RouteOperationResult.Success(routeId, null, null, $"Route #{routeId} has been {verb}.");
            }
            catch (SqlException ex) when (ex.Number is >= 50060 and <= 50069)
            {
                _logger.LogWarning(ex, "Blocked status change for route {RouteId}", routeId);
                return RouteOperationResult.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error changing status for route {RouteId}", routeId);
                throw;
            }
        }
    }
}
