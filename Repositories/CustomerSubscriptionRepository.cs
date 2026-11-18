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
    /// Production-grade SQL Server ADO.NET repository implementing ICustomerSubscriptionRepository.
    /// Manages multi-category subscriptions, normalized delivery days, custom price agreements,
    /// and effective date boundaries against dbo.CustomerSubscriptions, dbo.CustomerSubscriptionDeliveryDays,
    /// and dbo.CustomerCustomPricing.
    /// </summary>
    public class CustomerSubscriptionRepository : ICustomerSubscriptionRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<CustomerSubscriptionRepository> _logger;

        public CustomerSubscriptionRepository(ISqlConnectionFactory connectionFactory, ILogger<CustomerSubscriptionRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public CustomerSubscriptionRepository(IConfiguration configuration, ILogger<CustomerSubscriptionRepository> logger)
            : this(new SqlConnectionFactory(configuration), logger)
        {
        }

        public CustomerSubscriptionRepository(string connectionString, ILogger<CustomerSubscriptionRepository> logger)
            : this(new SqlConnectionFactory(connectionString), logger)
        {
        }

        public async Task<IReadOnlyList<CustomerSubscriptionModel>> GetActiveSubscriptionsByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT s.SubscriptionId, s.CustomerId, s.CategoryId, m.CategoryName,
                       s.Shift, s.PackagingQuantity, s.DailyLiquidVolumeLitres,
                       s.DeliverySchedule, s.DeliveryDays, s.EffectiveFrom, s.EffectiveTo, s.IsActive,
                       ISNULL(cp.CustomPrice, m.DefaultPrice) AS EffectivePricePerUnit
                FROM [dbo].[CustomerSubscriptions] s
                INNER JOIN [dbo].[MilkCategories] m ON s.CategoryId = m.CategoryId
                LEFT JOIN [dbo].[CustomerCustomPricing] cp ON s.CustomerId = cp.CustomerId
                    AND s.CategoryId = cp.CategoryId
                    AND cp.IsActive = 1
                    AND cp.EffectiveFrom <= CAST(GETDATE() AS DATE)
                    AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= CAST(GETDATE() AS DATE))
                WHERE s.CustomerId = @CustomerId
                  AND s.IsActive = 1
                  AND (s.EffectiveTo IS NULL OR s.EffectiveTo >= CAST(GETDATE() AS DATE))
                ORDER BY s.Shift ASC, m.DisplayOrder ASC, m.CategoryName ASC;";

            var list = new List<CustomerSubscriptionModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapSubscription(reader));
            }

            return list;
        }

        public async Task<IReadOnlyList<CustomerSubscriptionModel>> GetSubscriptionsForDateAsync(int customerId, DateOnly targetDate, string shift, CancellationToken cancellationToken = default)
        {
            // Convert .NET DayOfWeek (Sunday=0..Saturday=6) to ISO 8601 (Monday=1..Sunday=7)
            int isoDayOfWeek = (int)targetDate.DayOfWeek == 0 ? 7 : (int)targetDate.DayOfWeek;

            const string sql = @"
                SELECT s.SubscriptionId, s.CustomerId, s.CategoryId, m.CategoryName,
                       s.Shift, s.PackagingQuantity, s.DailyLiquidVolumeLitres,
                       s.DeliverySchedule, s.DeliveryDays, s.EffectiveFrom, s.EffectiveTo, s.IsActive,
                       ISNULL(cp.CustomPrice, m.DefaultPrice) AS EffectivePricePerUnit
                FROM [dbo].[CustomerSubscriptions] s
                INNER JOIN [dbo].[MilkCategories] m ON s.CategoryId = m.CategoryId
                INNER JOIN [dbo].[CustomerSubscriptionDeliveryDays] d ON s.SubscriptionId = d.SubscriptionId AND d.DayOfWeek = @DayOfWeek
                LEFT JOIN [dbo].[CustomerCustomPricing] cp ON s.CustomerId = cp.CustomerId
                    AND s.CategoryId = cp.CategoryId
                    AND cp.IsActive = 1
                    AND cp.EffectiveFrom <= @TargetDate
                    AND (cp.EffectiveTo IS NULL OR cp.EffectiveTo >= @TargetDate)
                WHERE s.CustomerId = @CustomerId
                  AND s.IsActive = 1
                  AND s.EffectiveFrom <= @TargetDate
                  AND (s.EffectiveTo IS NULL OR s.EffectiveTo >= @TargetDate)
                  AND (@Shift = 'Both' OR s.Shift = 'Both' OR s.Shift = @Shift)
                ORDER BY s.Shift ASC, m.DisplayOrder ASC;";

            var list = new List<CustomerSubscriptionModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@TargetDate", SqlDbType.Date).Value = targetDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;
            command.Parameters.Add("@DayOfWeek", SqlDbType.TinyInt).Value = (byte)isoDayOfWeek;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapSubscription(reader));
            }

            return list;
        }

        public async Task AddSubscriptionAsync(CustomerSubscriptionModel subscription, int createdByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);

            // Canonical day parsing & normalization
            var (schedule, canonicalDays, parsedDays) = NormalizeSchedule(subscription.DeliverySchedule, subscription.DeliveryDays);

            const string insertSubscriptionSql = @"
                INSERT INTO [dbo].[CustomerSubscriptions]
                    ([CustomerId], [CategoryId], [Shift], [PackagingQuantity], [DailyLiquidVolumeLitres],
                     [DeliverySchedule], [DeliveryDays], [EffectiveFrom], [EffectiveTo], [IsActive],
                     [CreatedAt], [CreatedBy])
                VALUES
                    (@CustomerId, @CategoryId, @Shift, @PackagingQuantity, @DailyLiquidVolumeLitres,
                     @DeliverySchedule, @DeliveryDays, @EffectiveFrom, @EffectiveTo, @IsActive,
                     SYSUTCDATETIME(), @CreatedBy);
                SELECT SCOPE_IDENTITY();";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            try
            {
                int subscriptionId;
                await using (var command = new SqlCommand(insertSubscriptionSql, connection, transaction))
                {
                    command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = subscription.CustomerId;
                    command.Parameters.Add("@CategoryId", SqlDbType.Int).Value = subscription.CategoryId;
                    command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = subscription.Shift;
                    command.Parameters.Add("@PackagingQuantity", SqlDbType.Decimal).Value = subscription.PackagingQuantity;
                    command.Parameters.Add("@DailyLiquidVolumeLitres", SqlDbType.Decimal).Value = subscription.DailyLiquidVolumeLitres;
                    command.Parameters.Add("@DeliverySchedule", SqlDbType.NVarChar, 20).Value = schedule;
                    command.Parameters.Add("@DeliveryDays", SqlDbType.VarChar, 20).Value = canonicalDays;
                    command.Parameters.Add("@EffectiveFrom", SqlDbType.Date).Value = subscription.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
                    command.Parameters.Add("@EffectiveTo", SqlDbType.Date).Value = subscription.EffectiveTo.HasValue
                        ? (object)subscription.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue)
                        : DBNull.Value;
                    command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = subscription.IsActive;
                    command.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = createdByUserId;

                    subscriptionId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                }

                // Insert normalized child weekday rows atomically in a single statement
                var valuesClauses = new List<string>();
                for (int i = 0; i < parsedDays.Count; i++)
                {
                    valuesClauses.Add($"(@SubscriptionId, @Day{i})");
                }

                var insertAllDaysSql = $@"
                    INSERT INTO [dbo].[CustomerSubscriptionDeliveryDays] ([SubscriptionId], [DayOfWeek])
                    VALUES {string.Join(", ", valuesClauses)};";

                await using (var dayCommand = new SqlCommand(insertAllDaysSql, connection, transaction))
                {
                    dayCommand.Parameters.Add("@SubscriptionId", SqlDbType.Int).Value = subscriptionId;
                    for (int i = 0; i < parsedDays.Count; i++)
                    {
                        dayCommand.Parameters.Add($"@Day{i}", SqlDbType.TinyInt).Value = (byte)parsedDays[i];
                    }

                    await dayCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                subscription.SubscriptionId = subscriptionId;
                subscription.DeliverySchedule = schedule;
                subscription.DeliveryDays = canonicalDays;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task UpdateSubscriptionScheduleAsync(int subscriptionId, string newSchedule, string? newDays, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(newSchedule))
            {
                throw new ArgumentException("Schedule cannot be null or whitespace.", nameof(newSchedule));
            }

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("[dbo].[usp_CustomerSubscription_UpdateSchedule]", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add("@SubscriptionId", SqlDbType.Int).Value = subscriptionId;
            command.Parameters.Add("@NewSchedule", SqlDbType.NVarChar, 20).Value = newSchedule;
            command.Parameters.Add("@NewDays", SqlDbType.VarChar, 20).Value = (object?)newDays ?? DBNull.Value;
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task EndSubscriptionAsync(int subscriptionId, DateOnly effectiveTo, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerSubscriptions]
                SET [EffectiveTo] = @EffectiveTo,
                    [IsActive] = 0,
                    [UpdatedAt] = SYSUTCDATETIME(),
                    [UpdatedBy] = @UpdatedBy
                WHERE [SubscriptionId] = @SubscriptionId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@SubscriptionId", SqlDbType.Int).Value = subscriptionId;
            command.Parameters.Add("@EffectiveTo", SqlDbType.Date).Value = effectiveTo.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = modifiedByUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CustomerCustomPriceModel>> GetCustomPricesByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT cp.PricingId, cp.CustomerId, cp.CategoryId, m.CategoryName,
                       m.DefaultPrice AS StandardDefaultPrice, cp.CustomPrice,
                       cp.EffectiveFrom, cp.EffectiveTo, cp.IsActive
                FROM [dbo].[CustomerCustomPricing] cp
                INNER JOIN [dbo].[MilkCategories] m ON cp.CategoryId = m.CategoryId
                WHERE cp.CustomerId = @CustomerId
                ORDER BY cp.EffectiveFrom DESC, m.CategoryName ASC;";

            var list = new List<CustomerCustomPriceModel>();
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerCustomPriceModel
                {
                    PricingId = reader.GetInt32(reader.GetOrdinal("PricingId")),
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                    CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                    StandardDefaultPrice = reader.GetDecimal(reader.GetOrdinal("StandardDefaultPrice")),
                    CustomPrice = reader.GetDecimal(reader.GetOrdinal("CustomPrice")),
                    EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveFrom"))),
                    EffectiveTo = reader.IsDBNull(reader.GetOrdinal("EffectiveTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveTo"))),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
                });
            }

            return list;
        }

        public async Task<decimal?> GetEffectivePriceAsync(int customerId, int categoryId, DateOnly targetDate, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                DECLARE @CustomPrice DECIMAL(18,2) = NULL;
                SELECT TOP 1 @CustomPrice = CustomPrice
                FROM [dbo].[CustomerCustomPricing]
                WHERE CustomerId = @CustomerId
                  AND CategoryId = @CategoryId
                  AND IsActive = 1
                  AND EffectiveFrom <= @TargetDate
                  AND (EffectiveTo IS NULL OR EffectiveTo >= @TargetDate)
                ORDER BY EffectiveFrom DESC;

                IF @CustomPrice IS NOT NULL
                BEGIN
                    SELECT @CustomPrice;
                END
                ELSE
                BEGIN
                    SELECT DefaultPrice FROM [dbo].[MilkCategories] WHERE CategoryId = @CategoryId;
                END";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId;
            command.Parameters.Add("@TargetDate", SqlDbType.Date).Value = targetDate.ToDateTime(TimeOnly.MinValue);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToDecimal(result);
            }

            return null;
        }

        public async Task SetCustomPriceAsync(CustomerCustomPriceModel priceModel, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(priceModel);

            const string closeExistingSql = @"
                UPDATE [dbo].[CustomerCustomPricing]
                SET [EffectiveTo] = DATEADD(day, -1, @EffectiveFrom),
                    [IsActive] = 0
                WHERE [CustomerId] = @CustomerId
                  AND [CategoryId] = @CategoryId
                  AND [IsActive] = 1
                  AND ([EffectiveTo] IS NULL OR [EffectiveTo] >= @EffectiveFrom);";

            const string insertSql = @"
                INSERT INTO [dbo].[CustomerCustomPricing]
                    ([CustomerId], [CategoryId], [CustomPrice], [EffectiveFrom], [EffectiveTo], [IsActive], [CreatedAt], [CreatedBy])
                VALUES
                    (@CustomerId, @CategoryId, @CustomPrice, @EffectiveFrom, @EffectiveTo, 1, SYSUTCDATETIME(), @CreatedBy);
                SELECT SCOPE_IDENTITY();";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            try
            {
                // Close existing active pricing
                await using (var closeCommand = new SqlCommand(closeExistingSql, connection, transaction))
                {
                    closeCommand.Parameters.Add("@CustomerId", SqlDbType.Int).Value = priceModel.CustomerId;
                    closeCommand.Parameters.Add("@CategoryId", SqlDbType.Int).Value = priceModel.CategoryId;
                    closeCommand.Parameters.Add("@EffectiveFrom", SqlDbType.Date).Value = priceModel.EffectiveFrom.ToDateTime(TimeOnly.MinValue);

                    await closeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                // Insert new custom pricing
                await using (var insertCommand = new SqlCommand(insertSql, connection, transaction))
                {
                    insertCommand.Parameters.Add("@CustomerId", SqlDbType.Int).Value = priceModel.CustomerId;
                    insertCommand.Parameters.Add("@CategoryId", SqlDbType.Int).Value = priceModel.CategoryId;
                    insertCommand.Parameters.Add("@CustomPrice", SqlDbType.Decimal).Value = priceModel.CustomPrice;
                    insertCommand.Parameters.Add("@EffectiveFrom", SqlDbType.Date).Value = priceModel.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
                    insertCommand.Parameters.Add("@EffectiveTo", SqlDbType.Date).Value = priceModel.EffectiveTo.HasValue
                        ? (object)priceModel.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue)
                        : DBNull.Value;
                    insertCommand.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = modifiedByUserId;

                    priceModel.PricingId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        public async Task EndCustomPriceAsync(int pricingId, DateOnly effectiveTo, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[CustomerCustomPricing]
                SET [EffectiveTo] = @EffectiveTo,
                    [IsActive] = 0
                WHERE [PricingId] = @PricingId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@PricingId", SqlDbType.Int).Value = pricingId;
            command.Parameters.Add("@EffectiveTo", SqlDbType.Date).Value = effectiveTo.ToDateTime(TimeOnly.MinValue);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static (string Schedule, string CanonicalDays, List<int> ParsedDays) NormalizeSchedule(string? rawSchedule, string? rawDays)
        {
            var schedule = string.IsNullOrWhiteSpace(rawSchedule) ? "Daily" : rawSchedule.Trim();

            if (schedule.Equals("Daily", StringComparison.OrdinalIgnoreCase))
            {
                return ("Daily", "1,2,3,4,5,6,7", new List<int> { 1, 2, 3, 4, 5, 6, 7 });
            }

            if (schedule.Equals("Weekdays", StringComparison.OrdinalIgnoreCase))
            {
                return ("Weekdays", "1,2,3,4,5", new List<int> { 1, 2, 3, 4, 5 });
            }

            if (schedule.Equals("Weekends", StringComparison.OrdinalIgnoreCase))
            {
                return ("Weekends", "6,7", new List<int> { 6, 7 });
            }

            // CustomDays: parse and sort unique integers between 1 and 7
            var set = new SortedSet<int>();
            if (!string.IsNullOrWhiteSpace(rawDays))
            {
                var parts = rawDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var p in parts)
                {
                    if (int.TryParse(p, out int d) && d >= 1 && d <= 7)
                    {
                        set.Add(d);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid weekday '{p}' specified in delivery schedule. Weekdays must be between 1 and 7.");
                    }
                }
            }

            if (set.Count == 0)
            {
                throw new ArgumentException("Custom schedule must specify at least one delivery day.");
            }

            // If exactly all 7, map to Daily
            if (set.Count == 7)
            {
                return ("Daily", "1,2,3,4,5,6,7", new List<int> { 1, 2, 3, 4, 5, 6, 7 });
            }

            // If exactly 1..5, map to Weekdays
            if (set.Count == 5 && set.Contains(1) && set.Contains(2) && set.Contains(3) && set.Contains(4) && set.Contains(5))
            {
                return ("Weekdays", "1,2,3,4,5", new List<int> { 1, 2, 3, 4, 5 });
            }

            // If exactly 6..7, map to Weekends
            if (set.Count == 2 && set.Contains(6) && set.Contains(7))
            {
                return ("Weekends", "6,7", new List<int> { 6, 7 });
            }

            var canonical = string.Join(",", set);
            return ("CustomDays", canonical, new List<int>(set));
        }

        private static CustomerSubscriptionModel MapSubscription(SqlDataReader reader)
        {
            return new CustomerSubscriptionModel
            {
                SubscriptionId = reader.GetInt32(reader.GetOrdinal("SubscriptionId")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                Shift = reader.GetString(reader.GetOrdinal("Shift")),
                PackagingQuantity = reader.GetDecimal(reader.GetOrdinal("PackagingQuantity")),
                DailyLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("DailyLiquidVolumeLitres")),
                DeliverySchedule = reader.GetString(reader.GetOrdinal("DeliverySchedule")),
                DeliveryDays = reader.GetString(reader.GetOrdinal("DeliveryDays")),
                EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveFrom"))),
                EffectiveTo = reader.IsDBNull(reader.GetOrdinal("EffectiveTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveTo"))),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                EffectivePricePerUnit = reader.GetDecimal(reader.GetOrdinal("EffectivePricePerUnit"))
            };
        }
    }
}
