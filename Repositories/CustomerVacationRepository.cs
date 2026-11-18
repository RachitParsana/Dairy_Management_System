using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Customer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Production-grade SQL Server ADO.NET repository implementing ICustomerVacationRepository.
    /// Manages customer absence requests, approval lifecycle, 6-pair conflict matrix overlap evaluation,
    /// and early resumption against dbo.CustomerVacations.
    /// </summary>
    public class CustomerVacationRepository : ICustomerVacationRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<CustomerVacationRepository> _logger;

        public CustomerVacationRepository(ISqlConnectionFactory connectionFactory, ILogger<CustomerVacationRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CustomerVacationRepository(IConfiguration configuration, ILogger<CustomerVacationRepository> logger)
            : this(new SqlConnectionFactory(configuration), logger)
        {
        }

        public CustomerVacationRepository(string connectionString, ILogger<CustomerVacationRepository> logger)
            : this(new SqlConnectionFactory(connectionString), logger)
        {
        }

        public async Task<CustomerVacationViewModel?> GetByIdAsync(int vacationId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT v.VacationId, v.CustomerId, c.CustomerCode, c.FullName AS CustomerName,
                       v.ReportedByDeliveryBoyId, uRep.FullName AS ReportedByName,
                       v.StartDate, v.EndDate, v.ResumedDate, v.Shift, v.Reason, v.Status,
                       v.ApprovedBy, uApp.FullName AS ApprovedByName, v.ApprovedAt,
                       v.RejectionReason, v.CreatedAt
                FROM [dbo].[CustomerVacations] v
                INNER JOIN [dbo].[Customers] c ON v.CustomerId = c.CustomerId
                LEFT JOIN [dbo].[DeliveryBoys] db ON v.ReportedByDeliveryBoyId = db.DeliveryBoyId
                LEFT JOIN [dbo].[Users] uRep ON db.UserId = uRep.UserId
                LEFT JOIN [dbo].[Users] uApp ON v.ApprovedBy = uApp.UserId
                WHERE v.VacationId = @VacationId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@VacationId", SqlDbType.Int).Value = vacationId;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapVacation(reader);
            }

            return null;
        }

        public async Task<IReadOnlyList<CustomerVacationViewModel>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT v.VacationId, v.CustomerId, c.CustomerCode, c.FullName AS CustomerName,
                       v.ReportedByDeliveryBoyId, uRep.FullName AS ReportedByName,
                       v.StartDate, v.EndDate, v.ResumedDate, v.Shift, v.Reason, v.Status,
                       v.ApprovedBy, uApp.FullName AS ApprovedByName, v.ApprovedAt,
                       v.RejectionReason, v.CreatedAt
                FROM [dbo].[CustomerVacations] v
                INNER JOIN [dbo].[Customers] c ON v.CustomerId = c.CustomerId
                LEFT JOIN [dbo].[DeliveryBoys] db ON v.ReportedByDeliveryBoyId = db.DeliveryBoyId
                LEFT JOIN [dbo].[Users] uRep ON db.UserId = uRep.UserId
                LEFT JOIN [dbo].[Users] uApp ON v.ApprovedBy = uApp.UserId
                WHERE v.CustomerId = @CustomerId
                ORDER BY v.StartDate DESC, v.VacationId DESC;";

            var list = new List<CustomerVacationViewModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapVacation(reader));
            }

            return list;
        }

        public async Task<IReadOnlyList<CustomerVacationViewModel>> GetPendingVacationsAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT v.VacationId, v.CustomerId, c.CustomerCode, c.FullName AS CustomerName,
                       v.ReportedByDeliveryBoyId, uRep.FullName AS ReportedByName,
                       v.StartDate, v.EndDate, v.ResumedDate, v.Shift, v.Reason, v.Status,
                       v.ApprovedBy, uApp.FullName AS ApprovedByName, v.ApprovedAt,
                       v.RejectionReason, v.CreatedAt
                FROM [dbo].[CustomerVacations] v
                INNER JOIN [dbo].[Customers] c ON v.CustomerId = c.CustomerId
                LEFT JOIN [dbo].[DeliveryBoys] db ON v.ReportedByDeliveryBoyId = db.DeliveryBoyId
                LEFT JOIN [dbo].[Users] uRep ON db.UserId = uRep.UserId
                LEFT JOIN [dbo].[Users] uApp ON v.ApprovedBy = uApp.UserId
                WHERE v.Status = 'Pending'
                ORDER BY v.StartDate ASC, v.CreatedAt ASC;";

            var list = new List<CustomerVacationViewModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapVacation(reader));
            }

            return list;
        }

        public async Task<bool> IsCustomerOnApprovedVacationAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[CustomerVacations]
                WHERE [CustomerId] = @CustomerId
                  AND [Status] = 'Approved'
                  AND [StartDate] <= @TargetDate
                  AND [EndDate] >= @TargetDate
                  AND ([Shift] = 'Both' OR @Shift = 'Both' OR [Shift] = @Shift)
                  AND ([ResumedDate] IS NULL OR @TargetDate < [ResumedDate]);";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@TargetDate", SqlDbType.Date).Value = targetDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
            return count > 0;
        }

        public async Task<bool> HasOverlappingVacationAsync(int customerId, DateOnly startDate, DateOnly endDate, string shift, int? excludeVacationId = null, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[CustomerVacations] WITH (HOLDLOCK)
                WHERE [CustomerId] = @CustomerId
                  AND [Status] IN ('Pending', 'Approved')
                  AND (@ExcludeId IS NULL OR [VacationId] != @ExcludeId)
                  AND ([Shift] = 'Both' OR @Shift = 'Both' OR [Shift] = @Shift)
                  AND ([StartDate] <= @EndDate AND [EndDate] >= @StartDate)
                  AND ([ResumedDate] IS NULL OR @StartDate < [ResumedDate]);";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;
            command.Parameters.Add("@ExcludeId", SqlDbType.Int).Value = (object?)excludeVacationId ?? DBNull.Value;

            var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
            return count > 0;
        }

        public async Task<int> CreateVacationRequestAsync(CustomerVacationViewModel vacation, int reportedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(vacation);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("[dbo].[usp_CustomerVacation_Create]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = vacation.CustomerId;
            command.Parameters.Add("@ReportedByDeliveryBoyId", SqlDbType.Int).Value = (object?)vacation.ReportedByDeliveryBoyId ?? DBNull.Value;
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = vacation.StartDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = vacation.EndDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = vacation.Shift;
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = (object?)vacation.Reason?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = vacation.Status;
            command.Parameters.Add("@ApprovedBy", SqlDbType.Int).Value = (object?)vacation.ApprovedBy ?? DBNull.Value;
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = reportedByUserId;

            var vacationIdParam = new SqlParameter("@VacationId", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            command.Parameters.Add(vacationIdParam);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            int vacationId = Convert.ToInt32(vacationIdParam.Value);
            vacation.VacationId = vacationId;
            return vacationId;
        }

        public async Task ApproveVacationAsync(int vacationId, int approvedByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerVacations]
                SET [Status] = 'Approved',
                    [ApprovedBy] = @ApprovedBy,
                    [ApprovedAt] = SYSUTCDATETIME(),
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @ApprovedBy
                WHERE [VacationId] = @VacationId AND [Status] = 'Pending';";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@VacationId", SqlDbType.Int).Value = vacationId;
            command.Parameters.Add("@ApprovedBy", SqlDbType.Int).Value = approvedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task RejectVacationAsync(int vacationId, string rejectionReason, int rejectedByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerVacations]
                SET [Status] = 'Rejected',
                    [RejectionReason] = @RejectionReason,
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @RejectedBy
                WHERE [VacationId] = @VacationId AND [Status] = 'Pending';";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@VacationId", SqlDbType.Int).Value = vacationId;
            command.Parameters.Add("@RejectionReason", SqlDbType.NVarChar, 250).Value = rejectionReason.Trim();
            command.Parameters.Add("@RejectedBy", SqlDbType.Int).Value = rejectedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ResumeEarlyVacationAsync(int vacationId, DateOnly resumedDate, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerVacations]
                SET [ResumedDate] = @ResumedDate,
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @UpdatedBy
                WHERE [VacationId] = @VacationId AND [Status] = 'Approved';";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@VacationId", SqlDbType.Int).Value = vacationId;
            command.Parameters.Add("@ResumedDate", SqlDbType.Date).Value = resumedDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task CancelVacationAsync(int vacationId, int cancelledByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerVacations]
                SET [Status] = 'Cancelled',
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @CancelledBy
                WHERE [VacationId] = @VacationId AND [Status] IN ('Pending', 'Approved');";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@VacationId", SqlDbType.Int).Value = vacationId;
            command.Parameters.Add("@CancelledBy", SqlDbType.Int).Value = cancelledByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static CustomerVacationViewModel MapVacation(SqlDataReader reader)
        {
            return new CustomerVacationViewModel
            {
                VacationId = reader.GetInt32(reader.GetOrdinal("VacationId")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                ReportedByDeliveryBoyId = reader.IsDBNull(reader.GetOrdinal("ReportedByDeliveryBoyId")) ? null : reader.GetInt32(reader.GetOrdinal("ReportedByDeliveryBoyId")),
                ReportedByName = reader.IsDBNull(reader.GetOrdinal("ReportedByName")) ? null : reader.GetString(reader.GetOrdinal("ReportedByName")),
                StartDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("StartDate"))),
                EndDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EndDate"))),
                ResumedDate = reader.IsDBNull(reader.GetOrdinal("ResumedDate")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("ResumedDate"))),
                Shift = reader.GetString(reader.GetOrdinal("Shift")),
                Reason = reader.IsDBNull(reader.GetOrdinal("Reason")) ? null : reader.GetString(reader.GetOrdinal("Reason")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy")) ? null : reader.GetInt32(reader.GetOrdinal("ApprovedBy")),
                ApprovedByName = reader.IsDBNull(reader.GetOrdinal("ApprovedByName")) ? null : reader.GetString(reader.GetOrdinal("ApprovedByName")),
                ApprovedAt = reader.IsDBNull(reader.GetOrdinal("ApprovedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ApprovedAt")),
                RejectionReason = reader.IsDBNull(reader.GetOrdinal("RejectionReason")) ? null : reader.GetString(reader.GetOrdinal("RejectionReason")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };
        }
    }
}
