using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
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
    /// Production-grade SQL Server ADO.NET repository implementing ICustomerRepository.
    /// Manages customer master persistence, concurrency-safe delivery boy assignments,
    /// effective date boundaries, and deactivation/reactivation transactions.
    /// </summary>
    public class CustomerRepository : ICustomerRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<CustomerRepository> _logger;

        public CustomerRepository(ISqlConnectionFactory connectionFactory, ILogger<CustomerRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CustomerRepository(IConfiguration configuration, ILogger<CustomerRepository> logger)
            : this(new SqlConnectionFactory(configuration), logger)
        {
        }

        public CustomerRepository(string connectionString, ILogger<CustomerRepository> logger)
            : this(new SqlConnectionFactory(connectionString), logger)
        {
        }

        public async Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT CustomerId, CustomerCode, FullName, FullNameGujarati, Phone, AlternatePhone,
                       Address, Area, Landmark, RouteId, CurrentDeliveryBoyId, DeliverySequence,
                       PreferredShift, Status, Notes, DeliveryStartDate, IsActive,
                       CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
                FROM [dbo].[Customers]
                WHERE [CustomerId] = @CustomerId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapCustomer(reader);
            }

            return null;
        }

        public async Task<Customer?> GetByCodeAsync(string customerCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(customerCode))
            {
                return null;
            }

            const string sql = @"
                SELECT CustomerId, CustomerCode, FullName, FullNameGujarati, Phone, AlternatePhone,
                       Address, Area, Landmark, RouteId, CurrentDeliveryBoyId, DeliverySequence,
                       PreferredShift, Status, Notes, DeliveryStartDate, IsActive,
                       CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
                FROM [dbo].[Customers]
                WHERE [CustomerCode] = @CustomerCode;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerCode", SqlDbType.VarChar, 20).Value = customerCode.Trim();

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapCustomer(reader);
            }

            return null;
        }

        public async Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomerListAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(criteria);

            var (whereClause, parameters) = BuildFilterClauses(criteria);

            var sql = $@"
                WITH ActiveSubscriptionsAgg AS (
                    SELECT CustomerId, ISNULL(SUM(DailyLiquidVolumeLitres), 0.000) AS ActiveDailyLitres
                    FROM [dbo].[CustomerSubscriptions]
                    WHERE [IsActive] = 1 AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= CAST(GETDATE() AS DATE))
                    GROUP BY CustomerId
                ),
                ActiveVacationAgg AS (
                    SELECT DISTINCT CustomerId
                    FROM [dbo].[CustomerVacations]
                    WHERE [Status] = 'Approved'
                      AND [StartDate] <= CAST(GETDATE() AS DATE)
                      AND [EndDate] >= CAST(GETDATE() AS DATE)
                      AND ([ResumedDate] IS NULL OR CAST(GETDATE() AS DATE) < [ResumedDate])
                )
                SELECT c.CustomerId, c.CustomerCode, c.FullName, c.FullNameGujarati, c.Phone, c.Address, c.Area,
                       c.RouteId, r.RouteName, c.CurrentDeliveryBoyId, u.FullName AS DeliveryBoyName,
                       c.DeliverySequence, c.PreferredShift, c.Status, c.IsActive,
                       CASE WHEN v.CustomerId IS NOT NULL THEN 1 ELSE 0 END AS IsOnVacationToday,
                       ISNULL(s.ActiveDailyLitres, 0.000) AS ActiveSubscriptionsDailyLitres
                FROM [dbo].[Customers] c
                INNER JOIN [dbo].[Routes] r ON c.RouteId = r.RouteId
                LEFT JOIN [dbo].[DeliveryBoys] db ON c.CurrentDeliveryBoyId = db.DeliveryBoyId
                LEFT JOIN [dbo].[Users] u ON db.UserId = u.UserId
                LEFT JOIN ActiveSubscriptionsAgg s ON c.CustomerId = s.CustomerId
                LEFT JOIN ActiveVacationAgg v ON c.CustomerId = v.CustomerId
                {whereClause}
                ORDER BY r.RouteName ASC, c.DeliverySequence ASC, c.FullName ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            var list = new List<CustomerListItemViewModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            foreach (var p in parameters)
            {
                command.Parameters.Add(p);
            }

            int offset = Math.Max(0, (criteria.PageNumber - 1) * criteria.PageSize);
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            command.Parameters.Add("@PageSize", SqlDbType.Int).Value = criteria.PageSize <= 0 ? 25 : criteria.PageSize;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerListItemViewModel
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    FullNameGujarati = reader.IsDBNull(reader.GetOrdinal("FullNameGujarati")) ? null : reader.GetString(reader.GetOrdinal("FullNameGujarati")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Address = reader.GetString(reader.GetOrdinal("Address")),
                    Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                    RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                    RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                    CurrentDeliveryBoyId = reader.IsDBNull(reader.GetOrdinal("CurrentDeliveryBoyId")) ? null : reader.GetInt32(reader.GetOrdinal("CurrentDeliveryBoyId")),
                    DeliveryBoyName = reader.IsDBNull(reader.GetOrdinal("DeliveryBoyName")) ? null : reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                    DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                    PreferredShift = reader.GetString(reader.GetOrdinal("PreferredShift")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    IsOnVacationToday = reader.GetInt32(reader.GetOrdinal("IsOnVacationToday")) == 1,
                    ActiveSubscriptionsDailyLitres = reader.GetDecimal(reader.GetOrdinal("ActiveSubscriptionsDailyLitres"))
                });
            }

            return list;
        }

        public async Task<int> GetCustomerCountAsync(CustomerFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(criteria);

            var (whereClause, parameters) = BuildFilterClauses(criteria);

            var sql = $@"
                WITH ActiveVacationAgg AS (
                    SELECT DISTINCT CustomerId
                    FROM [dbo].[CustomerVacations]
                    WHERE [Status] = 'Approved'
                      AND [StartDate] <= CAST(GETDATE() AS DATE)
                      AND [EndDate] >= CAST(GETDATE() AS DATE)
                      AND ([ResumedDate] IS NULL OR CAST(GETDATE() AS DATE) < [ResumedDate])
                )
                SELECT COUNT(1)
                FROM [dbo].[Customers] c
                INNER JOIN [dbo].[Routes] r ON c.RouteId = r.RouteId
                LEFT JOIN ActiveVacationAgg v ON c.CustomerId = v.CustomerId
                {whereClause};";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            foreach (var p in parameters)
            {
                command.Parameters.Add(p);
            }

            return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        }

        public async Task<IReadOnlyList<CustomerListItemViewModel>> GetCustomersByDeliveryBoyAsync(int deliveryBoyId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                WITH ActiveSubscriptionsAgg AS (
                    SELECT CustomerId, ISNULL(SUM(DailyLiquidVolumeLitres), 0.000) AS ActiveDailyLitres
                    FROM [dbo].[CustomerSubscriptions]
                    WHERE [IsActive] = 1 AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= CAST(GETDATE() AS DATE))
                    GROUP BY CustomerId
                ),
                ActiveVacationAgg AS (
                    SELECT DISTINCT CustomerId
                    FROM [dbo].[CustomerVacations]
                    WHERE [Status] = 'Approved'
                      AND [StartDate] <= CAST(GETDATE() AS DATE)
                      AND [EndDate] >= CAST(GETDATE() AS DATE)
                      AND ([ResumedDate] IS NULL OR CAST(GETDATE() AS DATE) < [ResumedDate])
                )
                SELECT c.CustomerId, c.CustomerCode, c.FullName, c.FullNameGujarati, c.Phone, c.Address, c.Area,
                       c.RouteId, r.RouteName, c.CurrentDeliveryBoyId, u.FullName AS DeliveryBoyName,
                       c.DeliverySequence, c.PreferredShift, c.Status, c.IsActive,
                       CASE WHEN v.CustomerId IS NOT NULL THEN 1 ELSE 0 END AS IsOnVacationToday,
                       ISNULL(s.ActiveDailyLitres, 0.000) AS ActiveSubscriptionsDailyLitres
                FROM [dbo].[Customers] c
                INNER JOIN [dbo].[Routes] r ON c.RouteId = r.RouteId
                LEFT JOIN [dbo].[DeliveryBoys] db ON c.CurrentDeliveryBoyId = db.DeliveryBoyId
                LEFT JOIN [dbo].[Users] u ON db.UserId = u.UserId
                LEFT JOIN ActiveSubscriptionsAgg s ON c.CustomerId = s.CustomerId
                LEFT JOIN ActiveVacationAgg v ON c.CustomerId = v.CustomerId
                WHERE c.CurrentDeliveryBoyId = @DeliveryBoyId AND c.IsActive = 1
                ORDER BY c.DeliverySequence ASC, c.FullName ASC;";

            var list = new List<CustomerListItemViewModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerListItemViewModel
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    FullNameGujarati = reader.IsDBNull(reader.GetOrdinal("FullNameGujarati")) ? null : reader.GetString(reader.GetOrdinal("FullNameGujarati")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Address = reader.GetString(reader.GetOrdinal("Address")),
                    Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                    RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                    RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                    CurrentDeliveryBoyId = reader.IsDBNull(reader.GetOrdinal("CurrentDeliveryBoyId")) ? null : reader.GetInt32(reader.GetOrdinal("CurrentDeliveryBoyId")),
                    DeliveryBoyName = reader.IsDBNull(reader.GetOrdinal("DeliveryBoyName")) ? null : reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                    DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                    PreferredShift = reader.GetString(reader.GetOrdinal("PreferredShift")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    IsOnVacationToday = reader.GetInt32(reader.GetOrdinal("IsOnVacationToday")) == 1,
                    ActiveSubscriptionsDailyLitres = reader.GetDecimal(reader.GetOrdinal("ActiveSubscriptionsDailyLitres"))
                });
            }

            return list;
        }

        public async Task<int> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(customer);

            const string insertCustomerSql = @"
                INSERT INTO [dbo].[Customers]
                    ([CustomerCode], [FullName], [FullNameGujarati], [Phone], [AlternatePhone],
                     [Address], [Area], [Landmark], [RouteId], [CurrentDeliveryBoyId],
                     [DeliverySequence], [PreferredShift], [Status], [Notes], [DeliveryStartDate],
                     [IsActive], [CreatedAt], [CreatedBy])
                VALUES
                    (@CustomerCode, @FullName, @FullNameGujarati, @Phone, @AlternatePhone,
                     @Address, @Area, @Landmark, @RouteId, @CurrentDeliveryBoyId,
                     @DeliverySequence, @PreferredShift, @Status, @Notes, @DeliveryStartDate,
                     @IsActive, SYSUTCDATETIME(), @CreatedBy);
                SELECT SCOPE_IDENTITY();";

            const string insertAssignmentSql = @"
                INSERT INTO [dbo].[CustomerDeliveryBoyAssignments]
                    ([CustomerId], [DeliveryBoyId], [AssignedFrom], [AssignedTo], [IsActive], [Notes], [CreatedAt], [CreatedBy])
                VALUES
                    (@CustomerId, @DeliveryBoyId, @AssignedFrom, NULL, 1, 'Initial assignment on customer onboarding', SYSUTCDATETIME(), @CreatedBy);";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            try
            {
                int newCustomerId;
                await using (var command = new SqlCommand(insertCustomerSql, connection, transaction))
                {
                    command.Parameters.Add("@CustomerCode", SqlDbType.VarChar, 20).Value = customer.CustomerCode.Trim();
                    command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = customer.FullName.Trim();
                    command.Parameters.Add("@FullNameGujarati", SqlDbType.NVarChar, 100).Value = (object?)customer.FullNameGujarati?.Trim() ?? DBNull.Value;
                    command.Parameters.Add("@Phone", SqlDbType.VarChar, 15).Value = customer.Phone.Trim();
                    command.Parameters.Add("@AlternatePhone", SqlDbType.VarChar, 15).Value = (object?)customer.AlternatePhone?.Trim() ?? DBNull.Value;
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 250).Value = customer.Address.Trim();
                    command.Parameters.Add("@Area", SqlDbType.NVarChar, 100).Value = (object?)customer.Area?.Trim() ?? DBNull.Value;
                    command.Parameters.Add("@Landmark", SqlDbType.NVarChar, 100).Value = (object?)customer.Landmark?.Trim() ?? DBNull.Value;
                    command.Parameters.Add("@RouteId", SqlDbType.Int).Value = customer.RouteId;
                    command.Parameters.Add("@CurrentDeliveryBoyId", SqlDbType.Int).Value = (object?)customer.CurrentDeliveryBoyId ?? DBNull.Value;
                    command.Parameters.Add("@DeliverySequence", SqlDbType.Int).Value = customer.DeliverySequence;
                    command.Parameters.Add("@PreferredShift", SqlDbType.NVarChar, 20).Value = customer.PreferredShift;
                    command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = customer.Status;
                    command.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value = (object?)customer.Notes?.Trim() ?? DBNull.Value;
                    command.Parameters.Add("@DeliveryStartDate", SqlDbType.Date).Value = customer.DeliveryStartDate == default
                        ? (object)DBNull.Value
                        : customer.DeliveryStartDate.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = customer.IsActive;
                    command.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = customer.CreatedBy;

                    newCustomerId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                }

                if (customer.CurrentDeliveryBoyId.HasValue)
                {
                    await using var assignCommand = new SqlCommand(insertAssignmentSql, connection, transaction);
                    assignCommand.Parameters.Add("@CustomerId", SqlDbType.Int).Value = newCustomerId;
                    assignCommand.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = customer.CurrentDeliveryBoyId.Value;
                    var assignedFrom = customer.DeliveryStartDate != default
                        ? customer.DeliveryStartDate
                        : DateOnly.FromDateTime(DateTime.Today);
                    assignCommand.Parameters.Add("@AssignedFrom", SqlDbType.Date).Value = assignedFrom.ToDateTime(TimeOnly.MinValue);
                    assignCommand.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = customer.CreatedBy;

                    await assignCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                customer.CustomerId = newCustomerId;
                return newCustomerId;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(customer);

            const string sql = @"
                UPDATE [dbo].[Customers]
                SET [FullName] = @FullName,
                    [FullNameGujarati] = @FullNameGujarati,
                    [Phone] = @Phone,
                    [AlternatePhone] = @AlternatePhone,
                    [Address] = @Address,
                    [Area] = @Area,
                    [Landmark] = @Landmark,
                    [RouteId] = @RouteId,
                    [DeliverySequence] = @DeliverySequence,
                    [PreferredShift] = @PreferredShift,
                    [Notes] = @Notes,
                    [Status] = @Status,
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @UpdatedBy
                WHERE [CustomerId] = @CustomerId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customer.CustomerId;
            command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = customer.FullName.Trim();
            command.Parameters.Add("@FullNameGujarati", SqlDbType.NVarChar, 100).Value = (object?)customer.FullNameGujarati?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@Phone", SqlDbType.VarChar, 15).Value = customer.Phone.Trim();
            command.Parameters.Add("@AlternatePhone", SqlDbType.VarChar, 15).Value = (object?)customer.AlternatePhone?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@Address", SqlDbType.NVarChar, 250).Value = customer.Address.Trim();
            command.Parameters.Add("@Area", SqlDbType.NVarChar, 100).Value = (object?)customer.Area?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@Landmark", SqlDbType.NVarChar, 100).Value = (object?)customer.Landmark?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = customer.RouteId;
            command.Parameters.Add("@DeliverySequence", SqlDbType.Int).Value = customer.DeliverySequence;
            command.Parameters.Add("@PreferredShift", SqlDbType.NVarChar, 20).Value = customer.PreferredShift;
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value = (object?)customer.Notes?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = customer.Status;
            command.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = (object?)customer.UpdatedBy ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task DeactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return DeactivateCustomerAsync(customerId, today, false, modifiedByUserId, cancellationToken);
        }

        public async Task DeactivateCustomerAsync(int customerId, DateOnly deactivationDate, bool deliveryAlreadyRecordedOnDate, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            string spName = deliveryAlreadyRecordedOnDate
                ? "[dbo].[usp_Customer_Deactivate_WithRecordedDelivery]"
                : "[dbo].[usp_Customer_Deactivate]";

            await using var command = new SqlCommand(spName, connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@DeactivationDate", SqlDbType.Date).Value = deactivationDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = deliveryAlreadyRecordedOnDate
                ? "Customer requested deactivation (recorded delivery billed)"
                : "Customer requested deactivation";
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> HasDeliveryOnDateAsync(int customerId, DateOnly date, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[DailyMilkDeliveries]
                WHERE [CustomerId] = @CustomerId
                  AND [EntryDate] = @TargetDate
                  AND [Status] IN ('Completed', 'Delivered');";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@TargetDate", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);

            var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
            return count > 0;
        }

        public Task ReactivateCustomerAsync(int customerId, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Reactivation requires explicit route, sequence, and delivery boy configuration. Use ReactivateCustomerAsync overload with logistics parameters.");
        }

        public async Task ReactivateCustomerAsync(int customerId, DateOnly reactivationDate, int routeId, int deliverySequence, int deliveryBoyId, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("[dbo].[usp_Customer_Reactivate]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@ReactivationDate", SqlDbType.Date).Value = reactivationDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = routeId;
            command.Parameters.Add("@DeliverySequence", SqlDbType.Int).Value = deliverySequence;
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 250).Value = "Customer reactivated by Admin";
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ReassignDeliveryBoyAsync(int customerId, int? newDeliveryBoyId, DateOnly effectiveDate, string? notes, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("[dbo].[usp_Customer_ReassignDeliveryBoy]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@NewDeliveryBoyId", SqlDbType.Int).Value = (object?)newDeliveryBoyId ?? DBNull.Value;
            command.Parameters.Add("@EffectiveDate", SqlDbType.Date).Value = effectiveDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 250).Value = (object?)notes?.Trim() ?? DBNull.Value;
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CustomerAssignmentHistoryModel>> GetAssignmentHistoryAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT a.AssignmentId, a.DeliveryBoyId, u.FullName AS DeliveryBoyName,
                       a.AssignedFrom, a.AssignedTo, a.IsActive, a.Notes, a.CreatedAt
                FROM [dbo].[CustomerDeliveryBoyAssignments] a
                INNER JOIN [dbo].[DeliveryBoys] db ON a.DeliveryBoyId = db.DeliveryBoyId
                INNER JOIN [dbo].[Users] u ON db.UserId = u.UserId
                WHERE a.CustomerId = @CustomerId
                ORDER BY a.AssignedFrom DESC, a.AssignmentId DESC;";

            var list = new List<CustomerAssignmentHistoryModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerAssignmentHistoryModel
                {
                    AssignmentId = reader.GetInt64(reader.GetOrdinal("AssignmentId")),
                    DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                    DeliveryBoyName = reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                    AssignedFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("AssignedFrom"))),
                    AssignedTo = reader.IsDBNull(reader.GetOrdinal("AssignedTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("AssignedTo"))),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }

            return list;
        }

        public async Task<string> GenerateNextCustomerCodeAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(CustomerCode, 6, 10) AS INT)), 0) + 1
                FROM [dbo].[Customers] WITH (UPDLOCK, HOLDLOCK)
                WHERE [CustomerCode] LIKE 'CUST-%';";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            var nextNum = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return $"CUST-{nextNum:D5}";
        }

        public async Task<bool> IsPhoneUniqueAsync(string phone, int? excludeCustomerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return true;
            }

            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[Customers]
                WHERE [Phone] = @Phone
                  AND (@ExcludeId IS NULL OR [CustomerId] != @ExcludeId)
                  AND [IsActive] = 1;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Phone", SqlDbType.VarChar, 15).Value = phone.Trim();
            command.Parameters.Add("@ExcludeId", SqlDbType.Int).Value = (object?)excludeCustomerId ?? DBNull.Value;

            var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
            return count == 0;
        }

        private static (string WhereClause, List<SqlParameter> Parameters) BuildFilterClauses(CustomerFilterCriteria criteria)
        {
            var sb = new StringBuilder("WHERE 1=1 ");
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrWhiteSpace(criteria.SearchTerm))
            {
                sb.Append("AND (c.FullName LIKE @Search OR c.FullNameGujarati LIKE @Search OR c.CustomerCode LIKE @Search OR c.Phone LIKE @Search OR c.Area LIKE @Search OR c.Address LIKE @Search) ");
                parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar, 100) { Value = $"%{criteria.SearchTerm.Trim()}%" });
            }

            if (criteria.RouteId.HasValue && criteria.RouteId.Value > 0)
            {
                sb.Append("AND c.RouteId = @RouteId ");
                parameters.Add(new SqlParameter("@RouteId", SqlDbType.Int) { Value = criteria.RouteId.Value });
            }

            if (criteria.DeliveryBoyId.HasValue && criteria.DeliveryBoyId.Value > 0)
            {
                sb.Append("AND c.CurrentDeliveryBoyId = @DeliveryBoyId ");
                parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = criteria.DeliveryBoyId.Value });
            }

            if (!string.IsNullOrWhiteSpace(criteria.Status) && !criteria.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("AND c.Status = @Status ");
                parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 20) { Value = criteria.Status.Trim() });
            }

            if (!string.IsNullOrWhiteSpace(criteria.Shift) && !criteria.Shift.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("AND (c.PreferredShift = @Shift OR c.PreferredShift = 'Both') ");
                parameters.Add(new SqlParameter("@Shift", SqlDbType.NVarChar, 20) { Value = criteria.Shift.Trim() });
            }

            if (criteria.IsOnVacation.HasValue)
            {
                if (criteria.IsOnVacation.Value)
                {
                    sb.Append("AND v.CustomerId IS NOT NULL ");
                }
                else
                {
                    sb.Append("AND v.CustomerId IS NULL ");
                }
            }

            return (sb.ToString(), parameters);
        }

        private static Customer MapCustomer(SqlDataReader reader)
        {
            return new Customer
            {
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                FullNameGujarati = reader.IsDBNull(reader.GetOrdinal("FullNameGujarati")) ? null : reader.GetString(reader.GetOrdinal("FullNameGujarati")),
                Phone = reader.GetString(reader.GetOrdinal("Phone")),
                AlternatePhone = reader.IsDBNull(reader.GetOrdinal("AlternatePhone")) ? null : reader.GetString(reader.GetOrdinal("AlternatePhone")),
                Address = reader.GetString(reader.GetOrdinal("Address")),
                Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                Landmark = reader.IsDBNull(reader.GetOrdinal("Landmark")) ? null : reader.GetString(reader.GetOrdinal("Landmark")),
                RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                CurrentDeliveryBoyId = reader.IsDBNull(reader.GetOrdinal("CurrentDeliveryBoyId")) ? null : reader.GetInt32(reader.GetOrdinal("CurrentDeliveryBoyId")),
                DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                PreferredShift = reader.GetString(reader.GetOrdinal("PreferredShift")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                DeliveryStartDate = reader.IsDBNull(reader.GetOrdinal("DeliveryStartDate"))
                    ? default
                    : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("DeliveryStartDate"))),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                UpdatedBy = reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? null : reader.GetInt32(reader.GetOrdinal("UpdatedBy"))
            };
        }
    }
}
