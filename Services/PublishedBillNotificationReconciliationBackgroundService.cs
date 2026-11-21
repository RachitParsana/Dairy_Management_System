using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Background hosted service that periodically reconciles published bills and ensures
    /// every published monthly bill has a corresponding BillPublished notification.
    /// Acts as the reliable fallback mechanism if in-process publication retries or immediate reconciliation fail.
    /// Strictly respects database-level idempotency (IX_Notifications_BillId_Type) and performs zero financial mutations.
    /// </summary>
    public class PublishedBillNotificationReconciliationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PublishedBillNotificationReconciliationBackgroundService> _logger;
        private readonly TimeSpan _period;
        private readonly TimeSpan _initialDelay;
        private readonly SemaphoreSlim _concurrencyLock = new(1, 1);

        public PublishedBillNotificationReconciliationBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<PublishedBillNotificationReconciliationBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Default to 30 minutes periodic interval, or override via configuration
            var intervalMinutes = configuration?.GetValue<double>("NotificationReconciliation:IntervalMinutes", 30.0) ?? 30.0;
            if (intervalMinutes <= 0) intervalMinutes = 30.0;
            _period = TimeSpan.FromMinutes(intervalMinutes);

            // Startup delay allows web application bootstrap and connection warm-up before first background cycle
            var startupDelaySeconds = configuration?.GetValue<double>("NotificationReconciliation:StartupDelaySeconds", 15.0) ?? 15.0;
            _initialDelay = TimeSpan.FromSeconds(Math.Max(1.0, startupDelaySeconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PublishedBillNotificationReconciliationBackgroundService started. Initial delay: {InitialDelay}s, Period: {PeriodMinutes}m.",
                _initialDelay.TotalSeconds, _period.TotalMinutes);

            try
            {
                // Initial startup delay
                await Task.Delay(_initialDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("PublishedBillNotificationReconciliationBackgroundService cancelled during initial startup delay.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunReconciliationCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Failures in background tasks must be logged and suppressed to protect host process stability
                    _logger.LogError(ex, "Unhandled exception during periodic published bill notification reconciliation cycle.");
                }

                try
                {
                    await Task.Delay(_period, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("PublishedBillNotificationReconciliationBackgroundService stopped gracefully.");
        }

        /// <summary>
        /// Executes a single reconciliation pass with concurrency protection against overlapping runs.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Number of reconciled bill notifications created</returns>
        public async Task<int> RunReconciliationCycleAsync(CancellationToken ct = default)
        {
            // Concurrency guard: avoid overlapping executions if previous reconciliation is taking longer
            if (!await _concurrencyLock.WaitAsync(0, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("Previous notification reconciliation run is still in progress. Skipping overlapping run.");
                return 0;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var connFactory = scope.ServiceProvider.GetRequiredService<Dairy_Management_System.Data.ISqlConnectionFactory>();

                // System/Admin User ID for audit tracking: dynamically resolve active Admin user
                int systemAdminUserId = 1004;
                try
                {
                    await using var conn = await connFactory.CreateAdminConnectionAsync(ct).ConfigureAwait(false);
                    await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                        "SELECT TOP 1 UserId FROM dbo.Users WHERE Role = 'Admin' AND IsActive = 1 ORDER BY UserId;", conn);
                    var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (scalar != null && scalar != DBNull.Value)
                    {
                        systemAdminUserId = Convert.ToInt32(scalar);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not dynamically resolve Admin UserId for background reconciliation; falling back to default {DefaultId}.", systemAdminUserId);
                }

                _logger.LogDebug("Starting periodic background reconciliation of published bill notifications...");
                var reconciledCount = await notificationService.ReconcilePublishedBillNotificationsAsync(systemAdminUserId, ct).ConfigureAwait(false);

                if (reconciledCount > 0)
                {
                    _logger.LogInformation("Background reconciliation successfully emitted {Count} missing BillPublished notifications.", reconciledCount);
                }
                else
                {
                    _logger.LogDebug("Background reconciliation cycle completed. No orphaned published bills found.");
                }

                return reconciledCount;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Notification reconciliation cycle was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute notification reconciliation cycle in background service.");
                throw;
            }
            finally
            {
                _concurrencyLock.Release();
            }
        }
    }
}
