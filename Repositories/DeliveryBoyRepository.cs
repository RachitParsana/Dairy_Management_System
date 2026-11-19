using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Customer;
using Dairy_Management_System.Models.DeliveryBoy;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Pure ADO.NET repository implementing IDeliveryBoyRepository.
    /// Executes under least-privilege db_dms_app role via compiled stored procedures.
    /// </summary>
    public class DeliveryBoyRepository : IDeliveryBoyRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DeliveryBoyRepository> _logger;

        public DeliveryBoyRepository(ISqlConnectionFactory connectionFactory, ILogger<DeliveryBoyRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> CreateDeliveryBoyAsync(CreateDeliveryBoyDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_Register", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 50) { Value = dto.Username.Trim() });
                command.Parameters.Add(new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 256) { Value = dto.PasswordHash });
                command.Parameters.Add(new SqlParameter("@FullName", SqlDbType.NVarChar, 100) { Value = dto.FullName.Trim() });
                command.Parameters.Add(new SqlParameter("@Phone", SqlDbType.VarChar, 15) { Value = dto.Phone.Trim() });
                command.Parameters.Add(new SqlParameter("@PrimaryRouteId", SqlDbType.Int) { Value = dto.PrimaryRouteId });
                command.Parameters.Add(new SqlParameter("@CompensationType", SqlDbType.NVarChar, 20) { Value = dto.CompensationType });
                command.Parameters.Add(new SqlParameter("@MonthlyBaseSalary", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = dto.MonthlyBaseSalary });
                command.Parameters.Add(new SqlParameter("@InitialCommissionRate", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = dto.InitialCommissionRate });
                command.Parameters.Add(new SqlParameter("@EffectiveFrom", SqlDbType.Date)
                {
                    Value = dto.EffectiveFrom.HasValue ? dto.EffectiveFrom.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@VehicleNumber", SqlDbType.NVarChar, 30)
                {
                    Value = (object?)dto.VehicleNumber?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 250)
                {
                    Value = (object?)dto.Notes?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = dto.AdminUserId });

                var outParam = new SqlParameter("@DeliveryBoyId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outParam);

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                return (int)outParam.Value;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error registering delivery boy {Username} (Error {Number})", dto.Username, ex.Number);
                throw;
            }
        }

        public async Task<DeliveryBoyDetailsDto?> GetByIdAsync(int deliveryBoyId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_GetDetails", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };
                command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

                // Result Set 1: Profile
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                var details = new DeliveryBoyDetailsDto
                {
                    DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                    UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    Username = reader.GetString(reader.GetOrdinal("Username")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString(reader.GetOrdinal("VehicleNumber")),
                    PrimaryRouteId = reader.IsDBNull(reader.GetOrdinal("PrimaryRouteId")) ? null : reader.GetInt32(reader.GetOrdinal("PrimaryRouteId")),
                    RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                    RouteCode = reader.IsDBNull(reader.GetOrdinal("RouteCode")) ? null : reader.GetString(reader.GetOrdinal("RouteCode")),
                    CompensationType = reader.GetString(reader.GetOrdinal("CompensationType")),
                    CurrentCommissionRate = reader.GetDecimal(reader.GetOrdinal("CurrentCommissionRate")),
                    MonthlyBaseSalary = reader.GetDecimal(reader.GetOrdinal("MonthlyBaseSalary")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastLoginAt")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                    CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByName")),
                    UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                    UpdatedBy = reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? null : reader.GetInt32(reader.GetOrdinal("UpdatedBy")),
                    UpdatedByName = reader.IsDBNull(reader.GetOrdinal("UpdatedByName")) ? null : reader.GetString(reader.GetOrdinal("UpdatedByName"))
                };

                // Result Set 2: Assigned Customers
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        details.AssignedCustomers.Add(new AssignedCustomerItemDto
                        {
                            CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                            CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                            FullName = reader.GetString(reader.GetOrdinal("FullName")),
                            FullNameGujarati = reader.IsDBNull(reader.GetOrdinal("FullNameGujarati")) ? null : reader.GetString(reader.GetOrdinal("FullNameGujarati")),
                            Phone = reader.GetString(reader.GetOrdinal("Phone")),
                            Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader.GetString(reader.GetOrdinal("Address")),
                            Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                            DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                            RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                            AssignedFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("AssignedFrom"))),
                            AssignedTo = reader.IsDBNull(reader.GetOrdinal("AssignedTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("AssignedTo"))),
                            AssignmentIsActive = reader.GetBoolean(reader.GetOrdinal("AssignmentIsActive"))
                        });
                    }
                }

                // Result Set 3: Commission Rate History
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        details.CommissionRateHistory.Add(new CommissionRateTierDto
                        {
                            RateId = reader.GetInt32(reader.GetOrdinal("RateId")),
                            DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                            CommissionRatePerLitre = reader.GetDecimal(reader.GetOrdinal("CommissionRatePerLitre")),
                            EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveFrom"))),
                            EffectiveTo = reader.IsDBNull(reader.GetOrdinal("EffectiveTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveTo"))),
                            Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                            CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                            CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByName"))
                        });
                    }
                }

                // Result Set 4: Recent Monthly Compensations
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        details.RecentCompensations.Add(new RecentCompensationItemDto
                        {
                            CompensationId = reader.GetInt64(reader.GetOrdinal("CompensationId")),
                            DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                            Month = reader.GetByte(reader.GetOrdinal("Month")),
                            Year = reader.GetInt16(reader.GetOrdinal("Year")),
                            CompensationType = reader.GetString(reader.GetOrdinal("CompensationType")),
                            EligibleCompletedLitres = reader.GetDecimal(reader.GetOrdinal("EligibleCompletedLitres")),
                            ApplicableCommissionRate = reader.GetDecimal(reader.GetOrdinal("ApplicableCommissionRate")),
                            CalculatedCommission = reader.GetDecimal(reader.GetOrdinal("CalculatedCommission")),
                            FixedSalaryAmount = reader.GetDecimal(reader.GetOrdinal("FixedSalaryAmount")),
                            Adjustments = reader.GetDecimal(reader.GetOrdinal("Adjustments")),
                            NetPayableAmount = reader.GetDecimal(reader.GetOrdinal("NetPayableAmount")),
                            Status = reader.GetString(reader.GetOrdinal("Status")),
                            FinalizedAt = reader.IsDBNull(reader.GetOrdinal("FinalizedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("FinalizedAt")),
                            PaidAt = reader.IsDBNull(reader.GetOrdinal("PaidAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PaidAt")),
                            PaymentReference = reader.IsDBNull(reader.GetOrdinal("PaymentReference")) ? null : reader.GetString(reader.GetOrdinal("PaymentReference"))
                        });
                    }
                }

                return details;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error getting delivery boy details {DeliveryBoyId} (Error {Number})", deliveryBoyId, ex.Number);
                throw;
            }
        }

        public async Task<IReadOnlyList<DeliveryBoyListItemDto>> GetDirectoryAsync(DeliveryBoyFilterCriteria criteria, CancellationToken ct = default)
        {
            var list = new List<DeliveryBoyListItemDto>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_GetDirectory", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@SearchTerm", SqlDbType.NVarChar, 100)
                {
                    Value = (object?)criteria?.SearchTerm?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.NVarChar, 20)
                {
                    Value = (object?)criteria?.StatusFilter?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@CompensationType", SqlDbType.NVarChar, 20)
                {
                    Value = (object?)criteria?.CompensationType?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int)
                {
                    Value = (object?)criteria?.RouteId ?? DBNull.Value
                });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new DeliveryBoyListItemDto
                    {
                        DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                        UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                        FullName = reader.GetString(reader.GetOrdinal("FullName")),
                        Username = reader.GetString(reader.GetOrdinal("Username")),
                        Phone = reader.GetString(reader.GetOrdinal("Phone")),
                        VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString(reader.GetOrdinal("VehicleNumber")),
                        PrimaryRouteId = reader.IsDBNull(reader.GetOrdinal("PrimaryRouteId")) ? null : reader.GetInt32(reader.GetOrdinal("PrimaryRouteId")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                        RouteCode = reader.IsDBNull(reader.GetOrdinal("RouteCode")) ? null : reader.GetString(reader.GetOrdinal("RouteCode")),
                        CompensationType = reader.GetString(reader.GetOrdinal("CompensationType")),
                        CurrentCommissionRate = reader.GetDecimal(reader.GetOrdinal("CurrentCommissionRate")),
                        MonthlyBaseSalary = reader.GetDecimal(reader.GetOrdinal("MonthlyBaseSalary")),
                        ActiveCustomerCount = reader.GetInt32(reader.GetOrdinal("ActiveCustomerCount")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }

                return list;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error getting delivery boy directory (Error {Number})", ex.Number);
                throw;
            }
        }

        public async Task<bool> UpdateProfileAsync(EditDeliveryBoyDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_UpdateProfile", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = dto.DeliveryBoyId });
                command.Parameters.Add(new SqlParameter("@FullName", SqlDbType.NVarChar, 100) { Value = dto.FullName.Trim() });
                command.Parameters.Add(new SqlParameter("@Phone", SqlDbType.VarChar, 15) { Value = dto.Phone.Trim() });
                command.Parameters.Add(new SqlParameter("@VehicleNumber", SqlDbType.NVarChar, 30)
                {
                    Value = (object?)dto.VehicleNumber?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@PrimaryRouteId", SqlDbType.Int) { Value = dto.PrimaryRouteId });
                command.Parameters.Add(new SqlParameter("@MonthlyBaseSalary", SqlDbType.Decimal)
                {
                    Precision = 18,
                    Scale = 2,
                    Value = (object?)dto.MonthlyBaseSalary ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = dto.AdminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error updating delivery boy {DeliveryBoyId} (Error {Number})", dto.DeliveryBoyId, ex.Number);
                throw;
            }
        }

        public async Task<bool> DeactivateAsync(int deliveryBoyId, int? reassignToDeliveryBoyId, string reason, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_Deactivate", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });
                command.Parameters.Add(new SqlParameter("@ReassignToDeliveryBoyId", SqlDbType.Int)
                {
                    Value = (object?)reassignToDeliveryBoyId ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 250) { Value = reason.Trim() });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error deactivating delivery boy {DeliveryBoyId} (Error {Number})", deliveryBoyId, ex.Number);
                throw;
            }
        }

        public async Task<bool> ReactivateAsync(int deliveryBoyId, decimal? newCommissionRate, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_Reactivate", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });
                command.Parameters.Add(new SqlParameter("@NewCommissionRate", SqlDbType.Decimal)
                {
                    Precision = 18,
                    Scale = 2,
                    Value = (object?)newCommissionRate ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error reactivating delivery boy {DeliveryBoyId} (Error {Number})", deliveryBoyId, ex.Number);
                throw;
            }
        }

        public async Task<IReadOnlyList<DeliveryBoyLookupItem>> GetActiveLookupAsync(CancellationToken ct = default)
        {
            var list = new List<DeliveryBoyLookupItem>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_DeliveryBoy_GetDirectory", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };
                command.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.NVarChar, 20) { Value = "Active" });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new DeliveryBoyLookupItem
                    {
                        DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                        FullName = reader.GetString(reader.GetOrdinal("FullName")),
                        Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                        VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString(reader.GetOrdinal("VehicleNumber")),
                        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
                    });
                }

                return list;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error retrieving active delivery boy lookup (Error {Number})", ex.Number);
                throw;
            }
        }
    }
}
