using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Dashboard;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    public class DashboardRepository : IDashboardRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DashboardRepository> _logger;

        public DashboardRepository(ISqlConnectionFactory connectionFactory, ILogger<DashboardRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AdminDashboardViewModel> GetAdminMetricsAsync(CancellationToken ct = default)
        {
            var viewModel = new AdminDashboardViewModel();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Dashboard_GetAdminMetrics", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

                // Result Set 1: KPI Summary & Month-Based Ageing
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    viewModel.TotalCustomers = reader.GetInt32(reader.GetOrdinal("TotalCustomers"));
                    viewModel.ActiveCustomers = reader.GetInt32(reader.GetOrdinal("ActiveCustomers"));
                    viewModel.ActiveDeliveryBoys = reader.GetInt32(reader.GetOrdinal("ActiveDeliveryBoys"));
                    viewModel.TodayDeliveredLitres = reader.GetDecimal(reader.GetOrdinal("TodayDeliveredLitres"));
                    viewModel.TodayCompletedDrops = reader.GetInt32(reader.GetOrdinal("TodayCompletedDrops"));
                    viewModel.TodayPendingDrops = reader.GetInt32(reader.GetOrdinal("TodayPendingDrops"));
                    viewModel.CurrentMonthBillingTotal = reader.GetDecimal(reader.GetOrdinal("CurrentMonthBillingTotal"));
                    viewModel.CurrentMonthCollectedTotal = reader.GetDecimal(reader.GetOrdinal("CurrentMonthCollectedTotal"));
                    viewModel.TotalOutstandingBalance = reader.GetDecimal(reader.GetOrdinal("TotalOutstandingBalance"));
                    viewModel.UnpaidInvoicesCount = reader.GetInt32(reader.GetOrdinal("UnpaidInvoicesCount"));
                    viewModel.UnpaidBalance1Month = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance1Month"));
                    viewModel.UnpaidBalance2Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance2Months"));
                    viewModel.UnpaidBalance3Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance3Months"));
                    viewModel.UnpaidBalance4To5Months = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance4To5Months"));
                    viewModel.UnpaidBalance6PlusMonths = reader.GetDecimal(reader.GetOrdinal("UnpaidBalance6PlusMonths"));
                }

                // Result Set 2: Shift Breakdown
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        viewModel.ShiftBreakdown.Add(new TodayDeliveryShiftBreakdownDto
                        {
                            Shift = reader.GetString(reader.GetOrdinal("Shift")),
                            DeliveredLitres = reader.GetDecimal(reader.GetOrdinal("DeliveredLitres")),
                            CompletedDeliveries = reader.GetInt32(reader.GetOrdinal("CompletedDeliveries")),
                            PendingDeliveries = reader.GetInt32(reader.GetOrdinal("PendingDeliveries"))
                        });
                    }
                }

                // Result Set 3: Recent Operational Delivery Ledger (Top 7 rows)
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        viewModel.RecentDeliveries.Add(new RecentOperationalDeliveryItemDto
                        {
                            DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                            EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                            Shift = reader.GetString(reader.GetOrdinal("Shift")),
                            CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                            RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                            TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                            TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                            DeliveryStatus = reader.GetString(reader.GetOrdinal("DeliveryStatus")),
                            PrimaryCategoryName = reader.GetString(reader.GetOrdinal("PrimaryCategoryName"))
                        });
                    }
                }

                return viewModel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Admin Dashboard metrics from database.");
                throw;
            }
        }

        public async Task<DeliveryBoyDashboardViewModel> GetDeliveryBoyMetricsAsync(int deliveryBoyId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Dashboard_GetDeliveryBoyMetrics", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return new DeliveryBoyDashboardViewModel
                    {
                        DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                        FullName = reader.GetString(reader.GetOrdinal("FullName")),
                        AssignedRouteId = reader.IsDBNull(reader.GetOrdinal("AssignedRouteId")) ? 0 : reader.GetInt32(reader.GetOrdinal("AssignedRouteId")),
                        AssignedRouteName = reader.GetString(reader.GetOrdinal("AssignedRouteName")),
                        TotalRouteCustomers = reader.GetInt32(reader.GetOrdinal("TotalRouteCustomers")),
                        TodayDeliveredLitres = reader.GetDecimal(reader.GetOrdinal("TodayDeliveredLitres")),
                        CompletedDropsToday = reader.GetInt32(reader.GetOrdinal("CompletedDropsToday")),
                        PendingDropsToday = reader.GetInt32(reader.GetOrdinal("PendingDropsToday")),
                        UnreadNotificationsCount = reader.GetInt32(reader.GetOrdinal("UnreadNotificationsCount"))
                    };
                }

                throw new InvalidOperationException($"No dashboard metrics returned for DeliveryBoyId {deliveryBoyId}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Delivery Boy metrics for ID {DeliveryBoyId}.", deliveryBoyId);
                throw;
            }
        }
    }
}
