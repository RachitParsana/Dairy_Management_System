using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Reports;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    public class ReportsRepository : IReportsRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<ReportsRepository> _logger;

        public ReportsRepository(ISqlConnectionFactory connectionFactory, ILogger<ReportsRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<MonthlyBillingVsCollectionTrendDto>> GetMonthlyBillingVsCollectionsAsync(int months = 6, CancellationToken ct = default)
        {
            var list = new List<MonthlyBillingVsCollectionTrendDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Reports_GetMonthlyBillingVsCollections", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@Months", SqlDbType.Int) { Value = months });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new MonthlyBillingVsCollectionTrendDto
                    {
                        BillingYear = reader.GetInt16(reader.GetOrdinal("BillingYear")),
                        BillingMonth = reader.GetByte(reader.GetOrdinal("BillingMonth")),
                        MonthName = reader.GetString(reader.GetOrdinal("MonthName")),
                        InvoicedAmount = reader.GetDecimal(reader.GetOrdinal("InvoicedAmount")),
                        CollectedAmount = reader.GetDecimal(reader.GetOrdinal("CollectedAmount"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Billing vs Collection trends for {Months} months.", months);
                throw;
            }
        }

        public async Task<IReadOnlyList<RouteMilkConsumptionReportDto>> GetRouteMilkConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? routeId = null, CancellationToken ct = default)
        {
            var list = new List<RouteMilkConsumptionReportDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Reports_GetRouteMilkConsumption", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = fromDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = toDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new RouteMilkConsumptionReportDto
                    {
                        RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                        RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                        ActiveCustomerCount = reader.GetInt32(reader.GetOrdinal("ActiveCustomerCount")),
                        TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                        MorningVolumeLitres = reader.GetDecimal(reader.GetOrdinal("MorningVolumeLitres")),
                        EveningVolumeLitres = reader.GetDecimal(reader.GetOrdinal("EveningVolumeLitres"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve route milk consumption report from {From} to {To}.", fromDate, toDate);
                throw;
            }
        }

        public async Task<IReadOnlyList<DailyCategoryShiftConsumptionDto>> GetDailyCategoryShiftConsumptionAsync(DateOnly fromDate, DateOnly toDate, int? categoryId = null, string? shift = null, CancellationToken ct = default)
        {
            var list = new List<DailyCategoryShiftConsumptionDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Reports_GetDailyCategoryShiftConsumption", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.Date) { Value = fromDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.Date) { Value = toDate.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@CategoryId", SqlDbType.Int) { Value = (object?)categoryId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@Shift", SqlDbType.NVarChar, 20) { Value = (object?)shift ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new DailyCategoryShiftConsumptionDto
                    {
                        EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                        CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                        Shift = reader.GetString(reader.GetOrdinal("Shift")),
                        Unit = reader.GetString(reader.GetOrdinal("Unit")),
                        DeliveredQuantity = reader.GetDecimal(reader.GetOrdinal("DeliveredQuantity")),
                        TotalMonetaryAmount = reader.GetDecimal(reader.GetOrdinal("TotalMonetaryAmount"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve category/shift consumption report from {From} to {To}.", fromDate, toDate);
                throw;
            }
        }

        public async Task<IReadOnlyList<CustomerBalanceAgingDto>> GetCustomerBalanceAgingAsync(int? routeId = null, CancellationToken ct = default)
        {
            var list = new List<CustomerBalanceAgingDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Reports_GetCustomerBalanceAging", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new CustomerBalanceAgingDto
                    {
                        CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                        CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                        FullName = reader.GetString(reader.GetOrdinal("FullName")),
                        Phone = reader.GetString(reader.GetOrdinal("Phone")),
                        RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                        UnpaidBalance1Month = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance1Month")),
                        UnpaidBalance2Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance2Months")),
                        UnpaidBalance3Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance3Months")),
                        UnpaidBalance4To5Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance4To5Months")),
                        UnpaidBalance6PlusMonths = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance6PlusMonths")),
                        TotalOutstanding = reader.GetDecimal(reader.GetOrdinal("TotalOutstanding")),
                        UnpaidBillsCount = reader.GetInt32(reader.GetOrdinal("UnpaidBillsCount"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve customer balance aging report for RouteId {RouteId}.", routeId);
                throw;
            }
        }
    }
}
