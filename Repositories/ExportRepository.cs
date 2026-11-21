using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Export;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    public class ExportRepository : IExportRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<ExportRepository> _logger;

        public ExportRepository(ISqlConnectionFactory connectionFactory, ILogger<ExportRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<CustomerExportItemDto>> GetCustomersExportAsync(int? routeId, bool? isActive, CancellationToken ct = default)
        {
            var list = new List<CustomerExportItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Export_GetCustomers", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = (object?)isActive ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new CustomerExportItemDto
                    {
                        CustomerCode = reader.IsDBNull(reader.GetOrdinal("CustomerCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("CustomerCode")),
                        FullName = reader.IsDBNull(reader.GetOrdinal("FullName")) ? string.Empty : reader.GetString(reader.GetOrdinal("FullName")),
                        Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? string.Empty : reader.GetString(reader.GetOrdinal("Phone")),
                        Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? string.Empty : reader.GetString(reader.GetOrdinal("Address")),
                        Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? string.Empty : reader.GetString(reader.GetOrdinal("Area")),
                        Landmark = reader.IsDBNull(reader.GetOrdinal("Landmark")) ? string.Empty : reader.GetString(reader.GetOrdinal("Landmark")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("RouteName")),
                        DeliveryBoyName = reader.IsDBNull(reader.GetOrdinal("DeliveryBoyName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                        DeliverySequence = reader.IsDBNull(reader.GetOrdinal("DeliverySequence")) ? 0 : reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                        Status = reader.IsDBNull(reader.GetOrdinal("Status")) ? string.Empty : reader.GetString(reader.GetOrdinal("Status")),
                        OutstandingBalance = reader.IsDBNull(reader.GetOrdinal("OutstandingBalance")) ? 0m : reader.GetDecimal(reader.GetOrdinal("OutstandingBalance")),
                        CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedAt")) ? DateTime.UtcNow : reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve customers export dataset.");
                throw;
            }
        }

        public async Task<IReadOnlyList<DailyDeliveryExportItemDto>> GetDailyDeliveriesExportAsync(DateOnly fromDate, DateOnly toDate, int? routeId, string? shift, CancellationToken ct = default)
        {
            var list = new List<DailyDeliveryExportItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Export_GetDailyDeliveries", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = fromDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = toDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@Shift", SqlDbType.NVarChar, 20) { Value = (object?)shift ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new DailyDeliveryExportItemDto
                    {
                        DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                        EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                        Shift = reader.GetString(reader.GetOrdinal("Shift")),
                        CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("RouteName")),
                        DeliveryBoyName = reader.IsDBNull(reader.GetOrdinal("DeliveryBoyName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                        TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                        TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        CommissionEarned = reader.GetDecimal(reader.GetOrdinal("CommissionEarned")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve daily deliveries export dataset.");
                throw;
            }
        }

        public async Task<IReadOnlyList<MonthlyBillExportItemDto>> GetMonthlyBillsExportAsync(byte month, short year, string? paymentStatus, int? routeId, CancellationToken ct = default)
        {
            var list = new List<MonthlyBillExportItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Export_GetMonthlyBills", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@Month", SqlDbType.TinyInt) { Value = month });
            command.Parameters.Add(new SqlParameter("@Year", SqlDbType.SmallInt) { Value = year });
            command.Parameters.Add(new SqlParameter("@PaymentStatus", SqlDbType.NVarChar, 20) { Value = (object?)paymentStatus ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new MonthlyBillExportItemDto
                    {
                        BillNumber = reader.GetString(reader.GetOrdinal("BillNumber")),
                        MonthYear = reader.GetString(reader.GetOrdinal("MonthYear")),
                        CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("RouteName")),
                        TotalLiquidMilkVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidMilkVolumeLitres")),
                        CurrentMonthInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthInvoiceAmount")),
                        PaidAmount = reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                        BalanceAmount = reader.GetDecimal(reader.GetOrdinal("BalanceAmount")),
                        PaymentDueDate = reader.IsDBNull(reader.GetOrdinal("PaymentDueDate")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDueDate"))),
                        PaymentStatus = reader.GetString(reader.GetOrdinal("PaymentStatus")),
                        PublishedAt = reader.IsDBNull(reader.GetOrdinal("PublishedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PublishedAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve monthly bills export dataset for {Month}/{Year}.", month, year);
                throw;
            }
        }

        public async Task<IReadOnlyList<PaymentExportItemDto>> GetPaymentsExportAsync(DateOnly? fromDate, DateOnly? toDate, string? paymentMode, CancellationToken ct = default)
        {
            var list = new List<PaymentExportItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Export_GetPayments", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = fromDate.HasValue ? fromDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = toDate.HasValue ? toDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
            command.Parameters.Add(new SqlParameter("@PaymentMode", SqlDbType.NVarChar, 20) { Value = (object?)paymentMode ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new PaymentExportItemDto
                    {
                        ReceiptNumber = reader.GetString(reader.GetOrdinal("ReceiptNumber")),
                        PaymentDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDate"))),
                        CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                        PaymentMode = reader.GetString(reader.GetOrdinal("PaymentMode")),
                        ReferenceNumber = reader.IsDBNull(reader.GetOrdinal("ReferenceNumber")) ? string.Empty : reader.GetString(reader.GetOrdinal("ReferenceNumber")),
                        Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                        ReceivedByAdmin = reader.IsDBNull(reader.GetOrdinal("ReceivedByAdmin")) ? string.Empty : reader.GetString(reader.GetOrdinal("ReceivedByAdmin")),
                        Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? string.Empty : reader.GetString(reader.GetOrdinal("Notes")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve payments export dataset.");
                throw;
            }
        }

        public async Task<IReadOnlyList<CompensationExportItemDto>> GetCompensationExportAsync(byte month, short year, CancellationToken ct = default)
        {
            var list = new List<CompensationExportItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Export_GetCompensation", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@Month", SqlDbType.TinyInt) { Value = month });
            command.Parameters.Add(new SqlParameter("@Year", SqlDbType.SmallInt) { Value = year });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new CompensationExportItemDto
                    {
                        CompensationId = reader.GetInt32(reader.GetOrdinal("CompensationId")),
                        DeliveryBoyName = reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                        Phone = reader.GetString(reader.GetOrdinal("Phone")),
                        RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? "Unassigned" : reader.GetString(reader.GetOrdinal("RouteName")),
                        MonthYear = reader.GetString(reader.GetOrdinal("MonthYear")),
                        CompensationType = reader.GetString(reader.GetOrdinal("CompensationType")),
                        TotalDeliveredLitres = reader.GetDecimal(reader.GetOrdinal("TotalDeliveredLitres")),
                        GrossRemuneration = reader.GetDecimal(reader.GetOrdinal("GrossRemuneration")),
                        Adjustments = reader.GetDecimal(reader.GetOrdinal("Adjustments")),
                        NetPayable = reader.GetDecimal(reader.GetOrdinal("NetPayable")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        PaymentReference = reader.IsDBNull(reader.GetOrdinal("PaymentReference")) ? string.Empty : reader.GetString(reader.GetOrdinal("PaymentReference")),
                        PaidAt = reader.IsDBNull(reader.GetOrdinal("PaidAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PaidAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve compensation export dataset for {Month}/{Year}.", month, year);
                throw;
            }
        }
    }
}
