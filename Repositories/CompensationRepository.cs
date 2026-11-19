using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Compensation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Pure ADO.NET repository implementing ICompensationRepository.
    /// Executes under least-privilege db_dms_app role via compiled stored procedures.
    /// Strictly respects ledger immutability and concurrency-safe procedures.
    /// </summary>
    public class CompensationRepository : ICompensationRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<CompensationRepository> _logger;

        public CompensationRepository(ISqlConnectionFactory connectionFactory, ILogger<CompensationRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MonthlyCompensationListItemDto>> GetCompensationsAsync(
            int month, int year, string? status, string? compensationType, CancellationToken ct = default)
        {
            var list = new List<MonthlyCompensationListItemDto>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_GetList", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@Month", SqlDbType.TinyInt) { Value = (byte)month });
                command.Parameters.Add(new SqlParameter("@Year", SqlDbType.SmallInt) { Value = (short)year });
                command.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 20)
                {
                    Value = (object?)status?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@CompensationType", SqlDbType.NVarChar, 20)
                {
                    Value = (object?)compensationType?.Trim() ?? DBNull.Value
                });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new MonthlyCompensationListItemDto
                    {
                        CompensationId = reader.GetInt64(reader.GetOrdinal("CompensationId")),
                        DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                        DeliveryBoyName = reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                        DeliveryBoyPhone = reader.GetString(reader.GetOrdinal("DeliveryBoyPhone")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                        RouteCode = reader.IsDBNull(reader.GetOrdinal("RouteCode")) ? null : reader.GetString(reader.GetOrdinal("RouteCode")),
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
                        FinalizedByName = reader.IsDBNull(reader.GetOrdinal("FinalizedByName")) ? null : reader.GetString(reader.GetOrdinal("FinalizedByName")),
                        PaidAt = reader.IsDBNull(reader.GetOrdinal("PaidAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PaidAt")),
                        PaymentReference = reader.IsDBNull(reader.GetOrdinal("PaymentReference")) ? null : reader.GetString(reader.GetOrdinal("PaymentReference")),
                        PaidByName = reader.IsDBNull(reader.GetOrdinal("PaidByName")) ? null : reader.GetString(reader.GetOrdinal("PaidByName")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }

                return list;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error getting compensations list for {Month}/{Year} (Error {Number})", month, year, ex.Number);
                throw;
            }
        }

        public async Task<MonthlyCompensationDetailsDto?> GetCompensationDetailsAsync(long compensationId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_GetDetails", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };
                command.Parameters.Add(new SqlParameter("@CompensationId", SqlDbType.BigInt) { Value = compensationId });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

                // Result Set 1: Header Statement Summary
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                var details = new MonthlyCompensationDetailsDto
                {
                    CompensationId = reader.GetInt64(reader.GetOrdinal("CompensationId")),
                    DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                    DeliveryBoyName = reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                    DeliveryBoyPhone = reader.GetString(reader.GetOrdinal("DeliveryBoyPhone")),
                    VehicleNumber = reader.IsDBNull(reader.GetOrdinal("VehicleNumber")) ? null : reader.GetString(reader.GetOrdinal("VehicleNumber")),
                    RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                    RouteCode = reader.IsDBNull(reader.GetOrdinal("RouteCode")) ? null : reader.GetString(reader.GetOrdinal("RouteCode")),
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
                    FinalizedByName = reader.IsDBNull(reader.GetOrdinal("FinalizedByName")) ? null : reader.GetString(reader.GetOrdinal("FinalizedByName")),
                    PaidAt = reader.IsDBNull(reader.GetOrdinal("PaidAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PaidAt")),
                    PaymentReference = reader.IsDBNull(reader.GetOrdinal("PaymentReference")) ? null : reader.GetString(reader.GetOrdinal("PaymentReference")),
                    PaidByName = reader.IsDBNull(reader.GetOrdinal("PaidByName")) ? null : reader.GetString(reader.GetOrdinal("PaidByName")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByName"))
                };

                // Result Set 2: Adjustments Breakdown
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        details.AdjustmentList.Add(new CompensationAdjustmentDto
                        {
                            AdjustmentId = reader.GetInt64(reader.GetOrdinal("AdjustmentId")),
                            CompensationId = reader.GetInt64(reader.GetOrdinal("CompensationId")),
                            AdjustmentType = reader.GetString(reader.GetOrdinal("AdjustmentType")),
                            AdjustmentAmount = reader.GetDecimal(reader.GetOrdinal("AdjustmentAmount")),
                            Reason = reader.GetString(reader.GetOrdinal("Reason")),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                            CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                            CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByName"))
                        });
                    }
                }

                // Result Set 3: Daily Delivery Breakdown
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        details.DailyDeliveries.Add(new DeliveryBoyDailyDeliveryBreakdownDto
                        {
                            DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                            EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                            Shift = reader.GetString(reader.GetOrdinal("Shift")),
                            CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                            CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                            Status = reader.GetString(reader.GetOrdinal("Status")),
                            TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                            EligibleLitreQuantity = reader.GetDecimal(reader.GetOrdinal("EligibleLitreQuantity")),
                            CommissionRateSnapshot = reader.GetDecimal(reader.GetOrdinal("CommissionRateSnapshot")),
                            EarnedCommission = reader.GetDecimal(reader.GetOrdinal("EarnedCommission")),
                            CustomerChargedAmount = reader.GetDecimal(reader.GetOrdinal("CustomerChargedAmount"))
                        });
                    }
                }

                return details;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error getting compensation details {CompensationId} (Error {Number})", compensationId, ex.Number);
                throw;
            }
        }

        public async Task<long> CalculateOrUpdateDraftAsync(int deliveryBoyId, int month, int year, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_CalculateMonthlyDraft", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 45
                };

                command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });
                command.Parameters.Add(new SqlParameter("@Month", SqlDbType.TinyInt) { Value = (byte)month });
                command.Parameters.Add(new SqlParameter("@Year", SqlDbType.SmallInt) { Value = (short)year });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                var outParam = new SqlParameter("@CompensationId", SqlDbType.BigInt)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outParam);

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                return (long)outParam.Value;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error calculating draft for DeliveryBoyId {DeliveryBoyId}, {Month}/{Year} (Error {Number})",
                    deliveryBoyId, month, year, ex.Number);
                throw;
            }
        }

        public async Task<int> BulkCalculateDraftsAsync(int month, int year, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_BulkCalculateDrafts", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                command.Parameters.Add(new SqlParameter("@Month", SqlDbType.TinyInt) { Value = (byte)month });
                command.Parameters.Add(new SqlParameter("@Year", SqlDbType.SmallInt) { Value = (short)year });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                var outParam = new SqlParameter("@TotalCalculated", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outParam);

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                return (int)outParam.Value;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error executing bulk draft calculation for {Month}/{Year} (Error {Number})", month, year, ex.Number);
                throw;
            }
        }

        public async Task<bool> ApplyAdjustmentAsync(long compensationId, string adjustmentType, decimal adjustmentAmount, string reason, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_ApplyAdjustment", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@CompensationId", SqlDbType.BigInt) { Value = compensationId });
                command.Parameters.Add(new SqlParameter("@AdjustmentType", SqlDbType.NVarChar, 30) { Value = adjustmentType.Trim() });
                command.Parameters.Add(new SqlParameter("@AdjustmentAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = adjustmentAmount });
                command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 250) { Value = reason.Trim() });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error applying adjustment to CompensationId {CompensationId} (Error {Number})", compensationId, ex.Number);
                throw;
            }
        }

        public async Task<IReadOnlyList<CompensationAdjustmentDto>> GetAdjustmentsAsync(long compensationId, CancellationToken ct = default)
        {
            var list = new List<CompensationAdjustmentDto>();

            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_GetAdjustments", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@CompensationId", SqlDbType.BigInt) { Value = compensationId });

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new CompensationAdjustmentDto
                    {
                        AdjustmentId = reader.GetInt64(reader.GetOrdinal("AdjustmentId")),
                        CompensationId = reader.GetInt64(reader.GetOrdinal("CompensationId")),
                        AdjustmentType = reader.GetString(reader.GetOrdinal("AdjustmentType")),
                        AdjustmentAmount = reader.GetDecimal(reader.GetOrdinal("AdjustmentAmount")),
                        Reason = reader.GetString(reader.GetOrdinal("Reason")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                        CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByName"))
                    });
                }

                return list;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error getting adjustments for CompensationId {CompensationId} (Error {Number})", compensationId, ex.Number);
                throw;
            }
        }

        public async Task<bool> FinalizeCompensationAsync(long compensationId, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_Finalize", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@CompensationId", SqlDbType.BigInt) { Value = compensationId });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error finalizing CompensationId {CompensationId} (Error {Number})", compensationId, ex.Number);
                throw;
            }
        }

        public async Task<bool> RecordPayoutAsync(long compensationId, DateTime paidAt, string paymentReference, string? notes, int adminUserId, CancellationToken ct = default)
        {
            try
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Compensation_RecordPayout", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 30
                };

                command.Parameters.Add(new SqlParameter("@CompensationId", SqlDbType.BigInt) { Value = compensationId });
                command.Parameters.Add(new SqlParameter("@PaidAt", SqlDbType.DateTime2) { Value = paidAt });
                command.Parameters.Add(new SqlParameter("@PaymentReference", SqlDbType.NVarChar, 50) { Value = paymentReference.Trim() });
                command.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 250)
                {
                    Value = (object?)notes?.Trim() ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL error recording payout for CompensationId {CompensationId} (Error {Number})", compensationId, ex.Number);
                throw;
            }
        }
    }
}
