using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Notification;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<NotificationRepository> _logger;

        public NotificationRepository(ISqlConnectionFactory connectionFactory, ILogger<NotificationRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<long> CreateAsync(Notification notification, CancellationToken ct = default)
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_Create", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(new SqlParameter("@Title", SqlDbType.NVarChar, 100) { Value = notification.Title });
            command.Parameters.Add(new SqlParameter("@Message", SqlDbType.NVarChar, 1000) { Value = notification.Message });
            command.Parameters.Add(new SqlParameter("@NotificationType", SqlDbType.NVarChar, 30) { Value = notification.NotificationType });
            command.Parameters.Add(new SqlParameter("@TargetRole", SqlDbType.NVarChar, 20) { Value = notification.TargetRole });
            command.Parameters.Add(new SqlParameter("@TargetRouteId", SqlDbType.Int) { Value = (object?)notification.TargetRouteId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@TargetDeliveryBoyId", SqlDbType.Int) { Value = (object?)notification.TargetDeliveryBoyId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = (object?)notification.CustomerId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@BillId", SqlDbType.BigInt) { Value = (object?)notification.BillId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@CustomerNameSnapshot", SqlDbType.NVarChar, 100) { Value = (object?)notification.CustomerNameSnapshot ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@UpdatedBillAmount", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)notification.UpdatedBillAmount ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@NewTotalOutstandingBalance", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)notification.NewTotalOutstandingBalance ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@BillingMonthYear", SqlDbType.VarChar, 10) { Value = (object?)notification.BillingMonthYear ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@PendingMonthsCount", SqlDbType.Int) { Value = (object?)notification.PendingMonthsCount ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = notification.CreatedBy });

            var outParam = new SqlParameter("@NewNotificationId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            command.Parameters.Add(outParam);

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return outParam.Value != DBNull.Value ? (long)outParam.Value : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create notification '{Title}' for role {TargetRole}.", notification.Title, notification.TargetRole);
                throw;
            }
        }

        public async Task<long> EmitBillPublishedAsync(long billId, int adminUserId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_EmitBillPublished", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@BillId", SqlDbType.BigInt) { Value = billId });
            command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

            var outParam = new SqlParameter("@NotificationId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            command.Parameters.Add(outParam);

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return outParam.Value != DBNull.Value ? (long)outParam.Value : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to emit bill publication notification for BillId {BillId}.", billId);
                throw;
            }
        }

        public async Task<int> ReconcilePublishedBillsAsync(int adminUserId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_ReconcilePublishedBills", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });
            var outParam = new SqlParameter("@ReconciledCount", SqlDbType.Int) { Direction = ParameterDirection.Output };
            command.Parameters.Add(outParam);

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return outParam.Value != DBNull.Value ? (int)outParam.Value : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconcile published bill notifications.");
                throw;
            }
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetForUserAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default)
        {
            var list = new List<NotificationListItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_GetForUser", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
            command.Parameters.Add(new SqlParameter("@Role", SqlDbType.NVarChar, 20) { Value = role });
            command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = (object?)deliveryBoyId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new NotificationListItemDto
                    {
                        NotificationId = reader.GetInt64(reader.GetOrdinal("NotificationId")),
                        Title = reader.GetString(reader.GetOrdinal("Title")),
                        Message = reader.GetString(reader.GetOrdinal("Message")),
                        NotificationType = reader.GetString(reader.GetOrdinal("NotificationType")),
                        TargetRole = reader.GetString(reader.GetOrdinal("TargetRole")),
                        TargetRouteId = reader.IsDBNull(reader.GetOrdinal("TargetRouteId")) ? null : reader.GetInt32(reader.GetOrdinal("TargetRouteId")),
                        TargetRouteName = reader.IsDBNull(reader.GetOrdinal("TargetRouteName")) ? null : reader.GetString(reader.GetOrdinal("TargetRouteName")),
                        TargetDeliveryBoyId = reader.IsDBNull(reader.GetOrdinal("TargetDeliveryBoyId")) ? null : reader.GetInt32(reader.GetOrdinal("TargetDeliveryBoyId")),
                        CustomerId = reader.IsDBNull(reader.GetOrdinal("CustomerId")) ? null : reader.GetInt32(reader.GetOrdinal("CustomerId")),
                        BillId = reader.IsDBNull(reader.GetOrdinal("BillId")) ? null : reader.GetInt64(reader.GetOrdinal("BillId")),
                        CustomerNameSnapshot = reader.IsDBNull(reader.GetOrdinal("CustomerNameSnapshot")) ? null : reader.GetString(reader.GetOrdinal("CustomerNameSnapshot")),
                        UpdatedBillAmount = reader.IsDBNull(reader.GetOrdinal("UpdatedBillAmount")) ? null : reader.GetDecimal(reader.GetOrdinal("UpdatedBillAmount")),
                        NewTotalOutstandingBalance = reader.IsDBNull(reader.GetOrdinal("NewTotalOutstandingBalance")) ? null : reader.GetDecimal(reader.GetOrdinal("NewTotalOutstandingBalance")),
                        BillingMonthYear = reader.IsDBNull(reader.GetOrdinal("BillingMonthYear")) ? null : reader.GetString(reader.GetOrdinal("BillingMonthYear")),
                        PendingMonthsCount = reader.IsDBNull(reader.GetOrdinal("PendingMonthsCount")) ? null : reader.GetInt32(reader.GetOrdinal("PendingMonthsCount")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        CreatedByName = reader.IsDBNull(reader.GetOrdinal("CreatedByName")) ? string.Empty : reader.GetString(reader.GetOrdinal("CreatedByName")),
                        IsAcknowledged = reader.GetInt32(reader.GetOrdinal("IsAcknowledged")) == 1,
                        AcknowledgedAt = reader.IsDBNull(reader.GetOrdinal("AcknowledgedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("AcknowledgedAt"))
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve notifications for UserId {UserId}.", userId);
                throw;
            }
        }

        public async Task<bool> AcknowledgeAsync(long notificationId, int userId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_Acknowledge", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@NotificationId", SqlDbType.BigInt) { Value = notificationId });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acknowledge notification {NotificationId} for UserId {UserId}.", notificationId, userId);
                throw;
            }
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetUnacknowledgedBillPopupsAsync(int deliveryBoyId, int userId, CancellationToken ct = default)
        {
            var list = new List<NotificationListItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_GetUnacknowledgedBillPopups", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(new NotificationListItemDto
                    {
                        NotificationId = reader.GetInt64(reader.GetOrdinal("NotificationId")),
                        Title = reader.GetString(reader.GetOrdinal("Title")),
                        Message = reader.GetString(reader.GetOrdinal("Message")),
                        BillId = reader.IsDBNull(reader.GetOrdinal("BillId")) ? null : reader.GetInt64(reader.GetOrdinal("BillId")),
                        CustomerId = reader.IsDBNull(reader.GetOrdinal("CustomerId")) ? null : reader.GetInt32(reader.GetOrdinal("CustomerId")),
                        CustomerNameSnapshot = reader.IsDBNull(reader.GetOrdinal("CustomerNameSnapshot")) ? null : reader.GetString(reader.GetOrdinal("CustomerNameSnapshot")),
                        UpdatedBillAmount = reader.IsDBNull(reader.GetOrdinal("UpdatedBillAmount")) ? null : reader.GetDecimal(reader.GetOrdinal("UpdatedBillAmount")),
                        NewTotalOutstandingBalance = reader.IsDBNull(reader.GetOrdinal("NewTotalOutstandingBalance")) ? null : reader.GetDecimal(reader.GetOrdinal("NewTotalOutstandingBalance")),
                        BillingMonthYear = reader.IsDBNull(reader.GetOrdinal("BillingMonthYear")) ? null : reader.GetString(reader.GetOrdinal("BillingMonthYear")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                        IsAcknowledged = false
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve unacknowledged bill popups for DeliveryBoyId {DeliveryBoyId}.", deliveryBoyId);
                throw;
            }
        }

        public async Task<bool> MarkPopupSeenAsync(long notificationId, int userId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_MarkPopupSeen", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@NotificationId", SqlDbType.BigInt) { Value = notificationId });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark popup seen for notification {NotificationId}, UserId {UserId}.", notificationId, userId);
                throw;
            }
        }

        public async Task<bool> MarkAllReadAsync(int userId, string role, int? deliveryBoyId, int? routeId, CancellationToken ct = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_MarkAllReadForUser", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
            command.Parameters.Add(new SqlParameter("@Role", SqlDbType.NVarChar, 20) { Value = role });
            command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = (object?)deliveryBoyId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = (object?)routeId ?? DBNull.Value });

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark all notifications as read for UserId {UserId}.", userId);
                throw;
            }
        }

        public async Task<IReadOnlyList<NotificationListItemDto>> GetAdminOverviewAsync(int limit = 50, CancellationToken ct = default)
        {
            var list = new List<NotificationListItemDto>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Notifications_GetAdminOverview", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = limit });

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                int ordId = reader.GetOrdinal("NotificationId");
                int ordTitle = reader.GetOrdinal("Title");
                int ordMsg = reader.GetOrdinal("Message");
                int ordType = reader.GetOrdinal("NotificationType");
                int ordRole = reader.GetOrdinal("TargetRole");
                int ordRouteName = reader.GetOrdinal("TargetRouteName");
                int ordDbName = reader.GetOrdinal("TargetDeliveryBoyName");
                int ordCustSnap = reader.GetOrdinal("CustomerNameSnapshot");
                int ordAmount = reader.GetOrdinal("UpdatedBillAmount");
                int ordMonthYear = reader.GetOrdinal("BillingMonthYear");
                int ordCreated = reader.GetOrdinal("CreatedAt");
                int ordCreatedBy = reader.GetOrdinal("CreatedByName");
                int ordAckCount = reader.GetOrdinal("AcknowledgementCount");
                int ordAckAt = reader.GetOrdinal("AcknowledgedAt");
                int ordAckNames = reader.GetOrdinal("AcknowledgedByNames");

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    int ackCount = reader.GetInt32(ordAckCount);
                    list.Add(new NotificationListItemDto
                    {
                        NotificationId = reader.GetInt64(ordId),
                        Title = reader.GetString(ordTitle),
                        Message = reader.GetString(ordMsg),
                        NotificationType = reader.GetString(ordType),
                        TargetRole = reader.GetString(ordRole),
                        TargetRouteName = reader.IsDBNull(ordRouteName) ? null : reader.GetString(ordRouteName),
                        TargetDeliveryBoyName = reader.IsDBNull(ordDbName) ? null : reader.GetString(ordDbName),
                        CustomerNameSnapshot = reader.IsDBNull(ordCustSnap) ? null : reader.GetString(ordCustSnap),
                        UpdatedBillAmount = reader.IsDBNull(ordAmount) ? null : reader.GetDecimal(ordAmount),
                        BillingMonthYear = reader.IsDBNull(ordMonthYear) ? null : reader.GetString(ordMonthYear),
                        CreatedAt = reader.GetDateTime(ordCreated),
                        CreatedByName = reader.IsDBNull(ordCreatedBy) ? string.Empty : reader.GetString(ordCreatedBy),
                        AcknowledgementCount = ackCount,
                        IsAcknowledged = ackCount > 0,
                        AcknowledgedAt = reader.IsDBNull(ordAckAt) ? null : reader.GetDateTime(ordAckAt),
                        AcknowledgedByNames = reader.IsDBNull(ordAckNames) ? null : reader.GetString(ordAckNames)
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve admin notification overview.");
                throw;
            }
        }
    }
}
