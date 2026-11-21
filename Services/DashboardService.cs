using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Dashboard;
using Dairy_Management_System.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IDashboardRepository _dashboardRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(
            IDashboardRepository dashboardRepository,
            INotificationRepository notificationRepository,
            ISqlConnectionFactory connectionFactory,
            ILogger<DashboardService> logger)
        {
            _dashboardRepository = dashboardRepository ?? throw new ArgumentNullException(nameof(dashboardRepository));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AdminDashboardViewModel> GetAdminDashboardViewModelAsync(CancellationToken ct = default)
        {
            return await _dashboardRepository.GetAdminMetricsAsync(ct).ConfigureAwait(false);
        }

        public async Task<DeliveryBoyDashboardViewModel> GetDeliveryBoyDashboardViewModelAsync(int userId, CancellationToken ct = default)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Valid authenticated UserId is required.", nameof(userId));
            }

            // Step 1: Resolve authenticated UserId to DeliveryBoyId and PrimaryRouteId
            int? deliveryBoyId = await ResolveDeliveryBoyIdByUserIdAsync(userId, ct).ConfigureAwait(false);
            if (!deliveryBoyId.HasValue)
            {
                _logger.LogWarning("User ID {UserId} is not an active delivery boy.", userId);
                throw new UnauthorizedAccessException("Logged in user is not associated with an active delivery partner profile.");
            }

            // Step 2: Query live metrics for resolved DeliveryBoyId
            var viewModel = await _dashboardRepository.GetDeliveryBoyMetricsAsync(deliveryBoyId.Value, ct).ConfigureAwait(false);

            // Step 3: Check for one-time unacknowledged bill publication popups targeting this delivery boy or route
            try
            {
                var popups = await _notificationRepository.GetUnacknowledgedBillPopupsAsync(deliveryBoyId.Value, userId, ct).ConfigureAwait(false);
                viewModel.UnacknowledgedBillPopups.AddRange(popups);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load bill publication popups for DeliveryBoyId {DeliveryBoyId}.", deliveryBoyId.Value);
            }

            return viewModel;
        }

        private async Task<int?> ResolveDeliveryBoyIdByUserIdAsync(int userId, CancellationToken ct)
        {
            const string sql = @"
                SELECT db.[DeliveryBoyId]
                FROM [dbo].[DeliveryBoys] db
                WHERE db.[UserId] = @UserId AND db.[IsActive] = 1;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }

            return null;
        }
    }
}
