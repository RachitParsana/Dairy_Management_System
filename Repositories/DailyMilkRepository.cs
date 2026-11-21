using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.DailyMilk;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("ConcurrencyRunner")]

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Production-grade SQL Server ADO.NET repository implementing IDailyMilkRepository.
    /// Manages daily milk runsheets, batch submissions, composite cursor continuous scrolling,
    /// effective-dated commission resolution, and immutable audit logs.
    /// </summary>
    public class DailyMilkRepository : IDailyMilkRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DailyMilkRepository> _logger;

        public DailyMilkRepository(ISqlConnectionFactory connectionFactory, ILogger<DailyMilkRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Deadlock Retry Policy

        /// <summary>
        /// Executes an asynchronous operation with full unit-of-work retry upon SQL Server error 1205 (deadlock).
        /// Retries up to 3 times with exponential backoff and jitter.
        /// Never catches or suppresses business validation exceptions.
        /// Internal test seam allowing ConcurrencyRunner to verify deadlock resilience.
        /// </summary>
        internal static async Task<T> ExecuteWithDeadlockRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
        {
            int attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (SqlException ex) when (ex.Number == 1205 && attempts <= maxRetries)
                {
                    int delayMs = (int)(Math.Pow(2, attempts) * 50) + Random.Shared.Next(0, 50);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
        }

        internal static async Task ExecuteWithDeadlockRetryAsync(Func<Task> operation, int maxRetries = 3)
        {
            await ExecuteWithDeadlockRetryAsync<bool>(async () =>
            {
                await operation().ConfigureAwait(false);
                return true;
            }, maxRetries).ConfigureAwait(false);
        }

        #endregion

        #region Table-Valued Parameter Construction

        private static DataTable CreateItemInputTable(IEnumerable<DailyMilkCategoryEntry> items, int customerId)
        {
            var table = new DataTable();
            table.Columns.Add("CustomerId", typeof(int));
            table.Columns.Add("CategoryId", typeof(int));
            table.Columns.Add("PackagingQuantity", typeof(decimal));

            foreach (var item in items)
            {
                table.Rows.Add(customerId, item.CategoryId, item.PackagingQuantity);
            }

            return table;
        }

        private static (DataTable Headers, DataTable Items) CreateBatchInputTables(IReadOnlyList<DailyMilkEntry> entries)
        {
            var headerTable = new DataTable();
            headerTable.Columns.Add("CustomerId", typeof(int));
            headerTable.Columns.Add("Status", typeof(string));
            headerTable.Columns.Add("NonDeliveryReason", typeof(string));
            headerTable.Columns.Add("IsUnscheduledDelivery", typeof(bool));

            var itemTable = new DataTable();
            itemTable.Columns.Add("CustomerId", typeof(int));
            itemTable.Columns.Add("CategoryId", typeof(int));
            itemTable.Columns.Add("PackagingQuantity", typeof(decimal));

            foreach (var entry in entries)
            {
                headerTable.Rows.Add(
                    entry.CustomerId,
                    entry.Status,
                    (object?)entry.NonDeliveryReason ?? DBNull.Value,
                    entry.IsUnscheduledDelivery);

                if (entry.Items != null && entry.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in entry.Items)
                    {
                        itemTable.Rows.Add(entry.CustomerId, item.CategoryId, item.PackagingQuantity);
                    }
                }
            }

            return (headerTable, itemTable);
        }

        #endregion

        #region Runsheet & Continuous Scrolling Queries

        public async Task<IReadOnlyList<DailyMilkListItemViewModel>> GetRosterAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            return await GetAdminRosterAsync(null, deliveryBoyId, entryDate, shift, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DailyMilkListItemViewModel>> GetAdminRosterAsync(
            int? routeId,
            int? deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            var list = new List<DailyMilkListItemViewModel>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("[dbo].[usp_DailyMilk_GetRoster]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = (object?)deliveryBoyId ?? DBNull.Value;
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)routeId ?? DBNull.Value;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new DailyMilkListItemViewModel
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    CustomerName = reader.GetString(reader.GetOrdinal("FullName")),
                    CustomerNameGujarati = reader.IsDBNull(reader.GetOrdinal("FullNameGujarati")) ? null : reader.GetString(reader.GetOrdinal("FullNameGujarati")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Address = reader.GetString(reader.GetOrdinal("Address")),
                    Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                    DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                    DeliveryId = reader.IsDBNull(reader.GetOrdinal("DeliveryId")) ? null : reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                    Status = reader.GetString(reader.GetOrdinal("DeliveryStatus")),
                    NonDeliveryReason = reader.IsDBNull(reader.GetOrdinal("NonDeliveryReason")) ? null : reader.GetString(reader.GetOrdinal("NonDeliveryReason")),
                    TotalLiquidLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                    TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                    IsUnscheduledDelivery = reader.GetBoolean(reader.GetOrdinal("IsUnscheduledDelivery")),
                    IsCorrected = reader.GetBoolean(reader.GetOrdinal("IsCorrected")),
                    IsOnApprovedVacation = reader.GetInt32(reader.GetOrdinal("IsOnVacation")) == 1,
                    VacationReason = reader.IsDBNull(reader.GetOrdinal("VacationReason")) ? null : reader.GetString(reader.GetOrdinal("VacationReason"))
                });
            }

            return list;
        }

        public async Task<DailyMilkEntry?> GetDeliveryByIdAsync(
            long deliveryId,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT d.DeliveryId, d.BatchId, d.CustomerId, d.DeliveryBoyId,
                       d.EntryDate, d.Shift, d.Status, d.VacationId, d.NonDeliveryReason,
                       d.CommissionRateSnapshot, d.TotalLiquidVolumeLitres, d.EligibleLitreQuantity,
                       d.TotalAmount, d.IsUnscheduledDelivery, d.AuthorizationId, d.AdminAuthorizedBy,
                       d.IsCorrected, d.LastCorrectedAt, d.LastCorrectedBy,
                       d.CreatedAt, d.CreatedBy, d.UpdatedAt, d.UpdatedBy
                FROM [dbo].[DailyMilkDeliveries] d
                WHERE d.DeliveryId = @DeliveryId;

                SELECT items.DeliveryItemId, items.DeliveryId, items.CategoryId, cat.CategoryName,
                       items.ProductTypeSnapshot, items.UnitOfMeasureSnapshot, items.PricingBasisSnapshot,
                       items.PackagingQuantity, items.BillingUnitEquivalentSnapshot, items.LitreEquivalentSnapshot,
                       items.LiquidVolumeLitres, items.UnitPriceSnapshot, items.SubTotal
                FROM [dbo].[DailyMilkDeliveryItems] items
                JOIN [dbo].[MilkCategories] cat ON items.CategoryId = cat.CategoryId
                WHERE items.DeliveryId = @DeliveryId
                ORDER BY cat.DisplayOrder ASC, items.CategoryId ASC;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryId", SqlDbType.BigInt).Value = deliveryId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var entry = MapDailyMilkEntry(reader);

            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                var items = new List<DailyMilkCategoryEntry>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    items.Add(MapDailyMilkCategoryEntry(reader));
                }
                entry.Items = items;
            }

            return entry;
        }

        public async Task<DailyMilkEntry?> GetDeliveryByCustomerDateShiftAsync(
            int customerId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT d.DeliveryId
                FROM [dbo].[DailyMilkDeliveries] d
                WHERE d.CustomerId = @CustomerId
                  AND d.EntryDate = @EntryDate
                  AND d.Shift = @Shift;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            long deliveryId = Convert.ToInt64(result);
            return await GetDeliveryByIdAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DailyMilkContinuousScrollResult<DailyMilkListItemViewModel>> GetDeliveriesContinuousAsync(
            DailyMilkFilterCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(criteria);

            var items = new List<DailyMilkListItemViewModel>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("[dbo].[usp_DailyMilk_GetDeliveriesContinuous]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = (object?)criteria.CustomerId ?? DBNull.Value;
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)criteria.RouteId ?? DBNull.Value;
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = (object?)criteria.DeliveryBoyId ?? DBNull.Value;
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = criteria.StartDate.HasValue ? criteria.StartDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = criteria.EndDate.HasValue ? criteria.EndDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = (object?)criteria.Shift ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = (object?)criteria.Status ?? DBNull.Value;
            command.Parameters.Add("@IsUnscheduledOnly", SqlDbType.Bit).Value = criteria.IsUnscheduled ?? false;
            command.Parameters.Add("@CursorEntryDate", SqlDbType.Date).Value = criteria.CursorEntryDate.HasValue ? criteria.CursorEntryDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            command.Parameters.Add("@CursorShift", SqlDbType.NVarChar, 20).Value = (object?)criteria.CursorShift ?? DBNull.Value;
            command.Parameters.Add("@CursorDeliveryId", SqlDbType.BigInt).Value = (object?)criteria.CursorDeliveryId ?? DBNull.Value;
            command.Parameters.Add("@PageSize", SqlDbType.Int).Value = criteria.FetchLimit;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new DailyMilkListItemViewModel
                {
                    DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                    Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? null : reader.GetString(reader.GetOrdinal("Area")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    NonDeliveryReason = reader.IsDBNull(reader.GetOrdinal("NonDeliveryReason")) ? null : reader.GetString(reader.GetOrdinal("NonDeliveryReason")),
                    TotalLiquidLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                    TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                    IsUnscheduledDelivery = reader.GetBoolean(reader.GetOrdinal("IsUnscheduledDelivery")),
                    IsCorrected = reader.GetBoolean(reader.GetOrdinal("IsCorrected")),
                    IsExistingEntry = true
                });
            }

            bool hasMore = items.Count > criteria.FetchLimit;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            DateOnly? nextCursorDate = null;
            string? nextCursorShift = null;
            long? nextCursorDeliveryId = null;

            if (items.Count > 0 && hasMore)
            {
                var lastItem = items[^1];
                nextCursorDeliveryId = lastItem.DeliveryId;
            }

            return new DailyMilkContinuousScrollResult<DailyMilkListItemViewModel>
            {
                Items = items,
                HasMore = hasMore,
                NextCursorEntryDate = nextCursorDate,
                NextCursorShift = nextCursorShift,
                NextCursorDeliveryId = nextCursorDeliveryId,
                TotalMatchingCount = items.Count
            };
        }

        #endregion

        #region Batch & Runsheet Persistence

        public async Task<DailyMilkBatch?> GetBatchAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT b.BatchId, b.DeliveryBoyId, b.RouteId,
                       b.EntryDate, b.Shift, b.Status, b.TotalAssignedCustomers, b.CompletedCount,
                       b.PendingCount, b.NotDeliveredCount, b.TotalLiquidVolumeLitres, b.TotalAmount,
                       b.StartedAt, b.SubmittedAt, b.SubmittedBy, b.CreatedAt, b.CreatedBy, b.UpdatedAt, b.UpdatedBy
                FROM [dbo].[DailyDeliveryBatches] b
                WHERE b.DeliveryBoyId = @DeliveryBoyId
                  AND b.EntryDate = @EntryDate
                  AND b.Shift = @Shift;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return MapDailyMilkBatch(reader);
        }

        public async Task<DailyMilkBatch?> GetBatchByIdAsync(
            long batchId,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT b.BatchId, b.DeliveryBoyId, b.RouteId,
                       b.EntryDate, b.Shift, b.Status, b.TotalAssignedCustomers, b.CompletedCount,
                       b.PendingCount, b.NotDeliveredCount, b.TotalLiquidVolumeLitres, b.TotalAmount,
                       b.StartedAt, b.SubmittedAt, b.SubmittedBy, b.CreatedAt, b.CreatedBy, b.UpdatedAt, b.UpdatedBy
                FROM [dbo].[DailyDeliveryBatches] b
                WHERE b.BatchId = @BatchId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@BatchId", SqlDbType.BigInt).Value = batchId;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return MapDailyMilkBatch(reader);
        }

        public async Task<long> SaveBatchAsync(
            DailyMilkBatch batch,
            IReadOnlyList<DailyMilkEntry> entries,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(entries);

            var (headerTable, itemTable) = CreateBatchInputTables(entries);

            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("[dbo].[usp_DailyMilk_SaveBatch]", connection);
                command.CommandType = CommandType.StoredProcedure;

                command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = batch.DeliveryBoyId;
                command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = batch.EntryDate.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = batch.Shift;
                command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = batch.Status;

                var pHeaders = command.Parameters.Add("@Headers", SqlDbType.Structured);
                pHeaders.TypeName = "dbo.DailyMilkDeliveryHeaderInputType";
                pHeaders.Value = headerTable;

                var pItems = command.Parameters.Add("@Items", SqlDbType.Structured);
                pItems.TypeName = "dbo.DailyMilkDeliveryItemInputType";
                pItems.Value = itemTable;

                command.Parameters.Add("@CreatedByUserId", SqlDbType.Int).Value = batch.CreatedBy;

                var pBatchId = command.Parameters.Add("@BatchId", SqlDbType.BigInt);
                pBatchId.Direction = ParameterDirection.Output;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                long newBatchId = Convert.ToInt64(pBatchId.Value);
                batch.BatchId = newBatchId;
                return newBatchId;
            }).ConfigureAwait(false);
        }

        public async Task<long> SaveDeliveryEntryAsync(
            DailyMilkEntry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var batch = new DailyMilkBatch
            {
                DeliveryBoyId = entry.DeliveryBoyId,
                EntryDate = entry.EntryDate,
                Shift = entry.Shift,
                Status = "Draft",
                CreatedBy = entry.CreatedBy
            };

            await SaveBatchAsync(batch, new[] { entry }, cancellationToken).ConfigureAwait(false);
            var saved = await GetDeliveryByCustomerDateShiftAsync(entry.CustomerId, entry.EntryDate, entry.Shift, cancellationToken).ConfigureAwait(false);
            return saved?.DeliveryId ?? 0;
        }

        #endregion

        #region Admin Corrections & Audit Trail

        public async Task CorrectDeliveryEntryAsync(
            long deliveryId,
            string newStatus,
            string? nonDeliveryReason,
            IReadOnlyList<DailyMilkCategoryEntry> updatedItems,
            string correctionReason,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            var itemTable = CreateItemInputTable(updatedItems, 0);

            await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("[dbo].[usp_DailyMilk_CorrectDelivery]", connection);
                command.CommandType = CommandType.StoredProcedure;

                command.Parameters.Add("@DeliveryId", SqlDbType.BigInt).Value = deliveryId;
                command.Parameters.Add("@NewStatus", SqlDbType.NVarChar, 20).Value = newStatus;
                command.Parameters.Add("@NonDeliveryReason", SqlDbType.NVarChar, 250).Value = (object?)nonDeliveryReason ?? DBNull.Value;

                var pItems = command.Parameters.Add("@UpdatedItems", SqlDbType.Structured);
                pItems.TypeName = "dbo.DailyMilkDeliveryItemInputType";
                pItems.Value = itemTable;

                command.Parameters.Add("@CorrectionReason", SqlDbType.NVarChar, 250).Value = correctionReason;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetCorrectionHistoryAsync(
            long deliveryId,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT c.CorrectionId, c.CorrectionBatchGuid, c.DeliveryId, c.DeliveryItemId,
                       c.CustomerId, cust.FullName AS CustomerName, c.EntryDate, c.Shift,
                       c.PriorStatus, c.NewStatus, c.CategoryId, cat.CategoryName,
                       c.PriorPackagingQuantity, c.NewPackagingQuantity,
                       c.PriorLiquidVolumeLitres, c.NewLiquidVolumeLitres,
                       c.PriorUnitPrice, c.NewUnitPrice,
                       c.PriorTotalAmount, c.NewTotalAmount,
                       c.Reason, c.CorrectedBy, u.FullName AS CorrectedByName, c.CorrectedAt
                FROM [dbo].[DailyMilkDeliveryCorrections] c
                JOIN [dbo].[Customers] cust ON c.CustomerId = cust.CustomerId
                LEFT JOIN [dbo].[MilkCategories] cat ON c.CategoryId = cat.CategoryId
                JOIN [dbo].[Users] u ON c.CorrectedBy = u.UserId
                WHERE c.DeliveryId = @DeliveryId
                ORDER BY c.CorrectedAt DESC, c.CorrectionId DESC;";

            var list = new List<DailyMilkCorrectionRecord>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryId", SqlDbType.BigInt).Value = deliveryId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapCorrectionRecord(reader));
            }

            return list;
        }

        public async Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetRecentCorrectionsAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT TOP (@Count)
                       c.CorrectionId, c.CorrectionBatchGuid, c.DeliveryId, c.DeliveryItemId,
                       c.CustomerId, cust.FullName AS CustomerName, c.EntryDate, c.Shift,
                       c.PriorStatus, c.NewStatus, c.CategoryId, cat.CategoryName,
                       c.PriorPackagingQuantity, c.NewPackagingQuantity,
                       c.PriorLiquidVolumeLitres, c.NewLiquidVolumeLitres,
                       c.PriorUnitPrice, c.NewUnitPrice,
                       c.PriorTotalAmount, c.NewTotalAmount,
                       c.Reason, c.CorrectedBy, u.FullName AS CorrectedByName, c.CorrectedAt
                FROM [dbo].[DailyMilkDeliveryCorrections] c
                JOIN [dbo].[Customers] cust ON c.CustomerId = cust.CustomerId
                LEFT JOIN [dbo].[MilkCategories] cat ON c.CategoryId = cat.CategoryId
                JOIN [dbo].[Users] u ON c.CorrectedBy = u.UserId
                ORDER BY c.CorrectedAt DESC, c.CorrectionId DESC;";

            var list = new List<DailyMilkCorrectionRecord>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Count", SqlDbType.Int).Value = count;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapCorrectionRecord(reader));
            }

            return list;
        }

        #endregion

        #region Unscheduled Delivery Authorization Management

        public async Task<long> CreateUnscheduledAuthorizationAsync(
            UnscheduledDeliveryAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(authorization);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("[dbo].[usp_CustomerUnscheduledDelivery_Authorize]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = authorization.CustomerId;
            command.Parameters.Add("@CategoryId", SqlDbType.Int).Value = (object?)authorization.CategoryId ?? DBNull.Value;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = authorization.EntryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@AuthorizedShift", SqlDbType.NVarChar, 20).Value = authorization.AuthorizedShift;
            command.Parameters.Add("@MaxQuantity", SqlDbType.Decimal).Value = (object?)authorization.MaxQuantity ?? DBNull.Value;
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = authorization.Reason;
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = authorization.AuthorizedBy;

            var pAuthId = command.Parameters.Add("@AuthorizationId", SqlDbType.BigInt);
            pAuthId.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            long authId = Convert.ToInt64(pAuthId.Value);
            authorization.AuthorizationId = authId;
            return authId;
        }

        public async Task<UnscheduledDeliveryAuthorization?> GetActiveUnscheduledAuthorizationAsync(
            int customerId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT TOP 1 a.AuthorizationId, a.CustomerId, c.FullName AS CustomerName,
                             a.CategoryId, cat.CategoryName, a.EntryDate, a.AuthorizedShift,
                             a.MaxQuantity, a.Reason, a.AuthorizedBy, u.FullName AS AuthorizedByName,
                             a.AuthorizedAt, a.IsRevoked, a.RevokedBy,
                             a.RevokedAt, a.RevokeReason
                FROM [dbo].[CustomerUnscheduledDeliveryAuthorizations] a
                JOIN [dbo].[Customers] c ON a.CustomerId = c.CustomerId
                LEFT JOIN [dbo].[MilkCategories] cat ON a.CategoryId = cat.CategoryId
                JOIN [dbo].[Users] u ON a.AuthorizedBy = u.UserId
                WHERE a.CustomerId = @CustomerId
                  AND a.EntryDate = @EntryDate
                  AND a.IsRevoked = 0
                  AND (a.AuthorizedShift = 'Both' OR a.AuthorizedShift = @Shift)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] con
                      WHERE con.AuthorizationId = a.AuthorizationId
                        AND con.ConsumedShift = @Shift
                  )
                ORDER BY a.AuthorizationId ASC;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return MapAuthorization(reader);
        }

        public async Task RecordAuthorizationConsumptionAsync(
            long authorizationId,
            long deliveryId,
            string shift,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions]
                    WHERE AuthorizationId = @AuthorizationId AND ConsumedShift = @Shift
                )
                BEGIN
                    INSERT INTO [dbo].[CustomerUnscheduledDeliveryAuthorizationConsumptions] (
                        AuthorizationId, DeliveryId, ConsumedShift, ConsumedAt
                    )
                    VALUES (@AuthorizationId, @DeliveryId, @Shift, SYSUTCDATETIME());
                END;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@AuthorizationId", SqlDbType.BigInt).Value = authorizationId;
            command.Parameters.Add("@DeliveryId", SqlDbType.BigInt).Value = deliveryId;
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task RevokeUnscheduledAuthorizationAsync(
            long authorizationId,
            string revokeReason,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("[dbo].[usp_CustomerUnscheduledDelivery_Revoke]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@AuthorizationId", SqlDbType.BigInt).Value = authorizationId;
            command.Parameters.Add("@RevokeReason", SqlDbType.NVarChar, 250).Value = revokeReason;
            command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Deterministic Pricing, Effective Commission & Guard Checks

        public async Task<decimal?> ResolveUnitPriceAsync(
            int customerId,
            int categoryId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                DECLARE @CustomPrice DECIMAL(18,2);
                SELECT TOP 1 @CustomPrice = CustomPrice
                FROM [dbo].[CustomerCustomPricing]
                WHERE CustomerId = @CustomerId
                  AND CategoryId = @CategoryId
                  AND IsActive = 1
                  AND EffectiveFrom <= @EntryDate
                  AND (EffectiveTo IS NULL OR EffectiveTo >= @EntryDate)
                ORDER BY EffectiveFrom DESC;

                IF (@CustomPrice IS NOT NULL)
                BEGIN
                    SELECT @CustomPrice;
                END
                ELSE
                BEGIN
                    SELECT DefaultPrice
                    FROM [dbo].[MilkCategories]
                    WHERE CategoryId = @CategoryId AND IsActive = 1;
                END;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@CategoryId", SqlDbType.Int).Value = categoryId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return Convert.ToDecimal(result);
        }

        public async Task<decimal?> ResolveCommissionRateAsync(
            int deliveryBoyId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                DECLARE @CompType NVARCHAR(20);
                SELECT @CompType = CompensationType
                FROM [dbo].[DeliveryBoys]
                WHERE DeliveryBoyId = @DeliveryBoyId;

                IF (@CompType = 'Fixed Salary')
                BEGIN
                    SELECT CAST(0.00 AS DECIMAL(18,2));
                END
                ELSE IF (@CompType = 'Commission')
                BEGIN
                    SELECT TOP 1 CommissionRatePerLitre
                    FROM [dbo].[DeliveryBoyCommissionRates]
                    WHERE DeliveryBoyId = @DeliveryBoyId
                      AND EffectiveFrom <= @EntryDate
                      AND (EffectiveTo IS NULL OR EffectiveTo >= @EntryDate)
                    ORDER BY EffectiveFrom DESC;
                END
                ELSE
                BEGIN
                    SELECT CAST(NULL AS DECIMAL(18,2));
                END;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return Convert.ToDecimal(result);
        }

        public async Task<IReadOnlyList<DeliveryBoyCommissionRate>> GetCommissionRateHistoryAsync(
            int deliveryBoyId,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT r.RateId, r.DeliveryBoyId, u.FullName AS DeliveryBoyName,
                       r.CommissionRatePerLitre AS CommissionRate, r.EffectiveFrom, r.EffectiveTo,
                       r.Notes, r.CreatedAt, r.CreatedBy, cu.FullName AS CreatedByName
                FROM [dbo].[DeliveryBoyCommissionRates] r
                JOIN [dbo].[DeliveryBoys] db ON r.DeliveryBoyId = db.DeliveryBoyId
                JOIN [dbo].[Users] u ON db.UserId = u.UserId
                JOIN [dbo].[Users] cu ON r.CreatedBy = cu.UserId
                WHERE r.DeliveryBoyId = @DeliveryBoyId
                ORDER BY r.EffectiveFrom DESC, r.RateId DESC;";

            var list = new List<DeliveryBoyCommissionRate>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new DeliveryBoyCommissionRate
                {
                    RateId = reader.GetInt32(reader.GetOrdinal("RateId")),
                    DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                    DeliveryBoyName = reader.GetString(reader.GetOrdinal("DeliveryBoyName")),
                    CommissionRate = reader.GetDecimal(reader.GetOrdinal("CommissionRate")),
                    EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveFrom"))),
                    EffectiveTo = reader.IsDBNull(reader.GetOrdinal("EffectiveTo")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EffectiveTo"))),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                    CreatedByName = reader.GetString(reader.GetOrdinal("CreatedByName"))
                });
            }

            return list;
        }

        public async Task<int> CreateCommissionRateAsync(
            DeliveryBoyCommissionRate rate,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(rate);

            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("[dbo].[usp_DeliveryBoy_CreateCommissionRate]", connection);
                command.CommandType = CommandType.StoredProcedure;

                command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = rate.DeliveryBoyId;
                command.Parameters.Add("@CommissionRate", SqlDbType.Decimal).Value = rate.CommissionRate;
                command.Parameters.Add("@EffectiveFrom", SqlDbType.Date).Value = rate.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add("@EffectiveTo", SqlDbType.Date).Value = rate.EffectiveTo.HasValue ? rate.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                command.Parameters.Add("@Notes", SqlDbType.NVarChar, 250).Value = (object?)rate.Notes ?? DBNull.Value;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = rate.CreatedBy;

                var pRateId = command.Parameters.Add("@RateId", SqlDbType.Int);
                pRateId.Direction = ParameterDirection.Output;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                int rateId = Convert.ToInt32(pRateId.Value);
                rate.RateId = rateId;
                return rateId;
            }).ConfigureAwait(false);
        }

        public async Task<bool> IsDeliveryDateBilledAndPublishedAsync(
            int customerId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[MonthlyBills]
                WHERE CustomerId = @CustomerId
                  AND @EntryDate BETWEEN PeriodStartDate AND PeriodEndDate
                  AND IsPublished = 1;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);

            var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
            return count > 0;
        }

        public async Task<DailyMilkSummaryViewModel> GetDailyShiftSummaryAsync(
            DateOnly entryDate,
            string shift,
            int? deliveryBoyId = null,
            CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("[dbo].[usp_DailyMilk_GetSummary]", connection);
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add("@EntryDate", SqlDbType.Date).Value = entryDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@Shift", SqlDbType.NVarChar, 20).Value = shift;
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = (object?)deliveryBoyId ?? DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DailyMilkSummaryViewModel
                {
                    EntryDate = entryDate,
                    Shift = shift,
                    TotalCustomers = reader.IsDBNull(reader.GetOrdinal("TotalAssignedCustomers")) ? 0 : reader.GetInt32(reader.GetOrdinal("TotalAssignedCustomers")),
                    CompletedDeliveries = reader.IsDBNull(reader.GetOrdinal("CompletedCount")) ? 0 : reader.GetInt32(reader.GetOrdinal("CompletedCount")),
                    PendingDeliveries = reader.IsDBNull(reader.GetOrdinal("PendingCount")) ? 0 : reader.GetInt32(reader.GetOrdinal("PendingCount")),
                    NotDeliveredCount = reader.IsDBNull(reader.GetOrdinal("NotDeliveredCount")) ? 0 : reader.GetInt32(reader.GetOrdinal("NotDeliveredCount")),
                    TotalLiquidLitres = reader.IsDBNull(reader.GetOrdinal("TotalLiquidVolumeLitres")) ? 0m : reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                    TotalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0m : reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                    EligibleCommissionLitres = reader.IsDBNull(reader.GetOrdinal("TotalLiquidVolumeLitres")) ? 0m : reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres"))
                };
            }

            return new DailyMilkSummaryViewModel
            {
                EntryDate = entryDate,
                Shift = shift
            };
        }

        public async Task<decimal> GetMonthlyDeliveredLitresAsync(
            int deliveryBoyId,
            int month,
            int year,
            CancellationToken cancellationToken = default)
        {
            var periodStart = new DateOnly(year, month, 1);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);

            const string sql = @"
                SELECT ISNULL(SUM(items.LiquidVolumeLitres), 0.000)
                FROM [dbo].[DailyMilkDeliveries] dmd
                JOIN [dbo].[DailyMilkDeliveryItems] items ON dmd.DeliveryId = items.DeliveryId
                WHERE dmd.DeliveryBoyId = @DeliveryBoyId
                  AND dmd.EntryDate BETWEEN @PeriodStartDate AND @PeriodEndDate
                  AND dmd.Status = 'Completed'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[CustomerVacations] v
                      WHERE v.CustomerId = dmd.CustomerId
                        AND v.Status = 'Approved'
                        AND dmd.EntryDate BETWEEN v.StartDate AND v.EndDate
                        AND (v.Shift = 'Both' OR v.Shift = dmd.Shift)
                  );";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = deliveryBoyId;
            command.Parameters.Add("@PeriodStartDate", SqlDbType.Date).Value = periodStart.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@PeriodEndDate", SqlDbType.Date).Value = periodEnd.ToDateTime(TimeOnly.MinValue);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return 0.000m;
            }

            return Convert.ToDecimal(result);
        }

        #endregion

        #region Mapping Helpers

        private static DailyMilkEntry MapDailyMilkEntry(SqlDataReader reader)
        {
            return new DailyMilkEntry
            {
                DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                BatchId = reader.IsDBNull(reader.GetOrdinal("BatchId")) ? null : reader.GetInt64(reader.GetOrdinal("BatchId")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                Shift = reader.GetString(reader.GetOrdinal("Shift")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                VacationId = reader.IsDBNull(reader.GetOrdinal("VacationId")) ? null : reader.GetInt32(reader.GetOrdinal("VacationId")),
                NonDeliveryReason = reader.IsDBNull(reader.GetOrdinal("NonDeliveryReason")) ? null : reader.GetString(reader.GetOrdinal("NonDeliveryReason")),
                CommissionRateSnapshot = reader.GetDecimal(reader.GetOrdinal("CommissionRateSnapshot")),
                TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                EligibleLitreQuantity = reader.GetDecimal(reader.GetOrdinal("EligibleLitreQuantity")),
                TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                IsUnscheduledDelivery = reader.GetBoolean(reader.GetOrdinal("IsUnscheduledDelivery")),
                AdminAuthorizedBy = reader.IsDBNull(reader.GetOrdinal("AdminAuthorizedBy")) ? null : reader.GetInt32(reader.GetOrdinal("AdminAuthorizedBy")),
                IsCorrected = reader.GetBoolean(reader.GetOrdinal("IsCorrected")),
                LastCorrectedAt = reader.IsDBNull(reader.GetOrdinal("LastCorrectedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastCorrectedAt")),
                LastCorrectedBy = reader.IsDBNull(reader.GetOrdinal("LastCorrectedBy")) ? null : reader.GetInt32(reader.GetOrdinal("LastCorrectedBy")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                UpdatedBy = reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? null : reader.GetInt32(reader.GetOrdinal("UpdatedBy"))
            };
        }

        private static DailyMilkCategoryEntry MapDailyMilkCategoryEntry(SqlDataReader reader)
        {
            return new DailyMilkCategoryEntry
            {
                DeliveryItemId = reader.GetInt64(reader.GetOrdinal("DeliveryItemId")),
                DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                ProductTypeSnapshot = reader.GetString(reader.GetOrdinal("ProductTypeSnapshot")),
                UnitOfMeasureSnapshot = reader.GetString(reader.GetOrdinal("UnitOfMeasureSnapshot")),
                PricingBasisSnapshot = reader.GetString(reader.GetOrdinal("PricingBasisSnapshot")),
                PackagingQuantity = reader.GetDecimal(reader.GetOrdinal("PackagingQuantity")),
                BillingUnitEquivalentSnapshot = reader.GetDecimal(reader.GetOrdinal("BillingUnitEquivalentSnapshot")),
                LitreEquivalentSnapshot = reader.GetDecimal(reader.GetOrdinal("LitreEquivalentSnapshot")),
                LiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("LiquidVolumeLitres")),
                UnitPriceSnapshot = reader.GetDecimal(reader.GetOrdinal("UnitPriceSnapshot")),
                SubTotal = reader.GetDecimal(reader.GetOrdinal("SubTotal"))
            };
        }

        private static DailyMilkBatch MapDailyMilkBatch(SqlDataReader reader)
        {
            return new DailyMilkBatch
            {
                BatchId = reader.GetInt64(reader.GetOrdinal("BatchId")),
                DeliveryBoyId = reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                Shift = reader.GetString(reader.GetOrdinal("Shift")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                TotalAssignedCustomers = reader.GetInt32(reader.GetOrdinal("TotalAssignedCustomers")),
                CompletedCount = reader.GetInt32(reader.GetOrdinal("CompletedCount")),
                PendingCount = reader.GetInt32(reader.GetOrdinal("PendingCount")),
                NotDeliveredCount = reader.GetInt32(reader.GetOrdinal("NotDeliveredCount")),
                TotalLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidVolumeLitres")),
                TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                StartedAt = reader.GetDateTime(reader.GetOrdinal("StartedAt")),
                SubmittedAt = reader.IsDBNull(reader.GetOrdinal("SubmittedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("SubmittedAt")),
                SubmittedBy = reader.IsDBNull(reader.GetOrdinal("SubmittedBy")) ? null : reader.GetInt32(reader.GetOrdinal("SubmittedBy")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                UpdatedBy = reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? null : reader.GetInt32(reader.GetOrdinal("UpdatedBy"))
            };
        }

        private static DailyMilkCorrectionRecord MapCorrectionRecord(SqlDataReader reader)
        {
            return new DailyMilkCorrectionRecord
            {
                CorrectionId = reader.GetInt64(reader.GetOrdinal("CorrectionId")),
                CorrectionBatchGuid = reader.GetGuid(reader.GetOrdinal("CorrectionBatchGuid")),
                DeliveryId = reader.GetInt64(reader.GetOrdinal("DeliveryId")),
                DeliveryItemId = reader.IsDBNull(reader.GetOrdinal("DeliveryItemId")) ? null : reader.GetInt64(reader.GetOrdinal("DeliveryItemId")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                Shift = reader.GetString(reader.GetOrdinal("Shift")),
                PriorStatus = reader.GetString(reader.GetOrdinal("PriorStatus")),
                NewStatus = reader.GetString(reader.GetOrdinal("NewStatus")),
                CategoryId = reader.IsDBNull(reader.GetOrdinal("CategoryId")) ? null : reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
                PriorPackagingQuantity = reader.IsDBNull(reader.GetOrdinal("PriorPackagingQuantity")) ? null : reader.GetDecimal(reader.GetOrdinal("PriorPackagingQuantity")),
                NewPackagingQuantity = reader.IsDBNull(reader.GetOrdinal("NewPackagingQuantity")) ? null : reader.GetDecimal(reader.GetOrdinal("NewPackagingQuantity")),
                PriorLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("PriorLiquidVolumeLitres")),
                NewLiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("NewLiquidVolumeLitres")),
                PriorUnitPrice = reader.IsDBNull(reader.GetOrdinal("PriorUnitPrice")) ? null : reader.GetDecimal(reader.GetOrdinal("PriorUnitPrice")),
                NewUnitPrice = reader.IsDBNull(reader.GetOrdinal("NewUnitPrice")) ? null : reader.GetDecimal(reader.GetOrdinal("NewUnitPrice")),
                PriorTotalAmount = reader.GetDecimal(reader.GetOrdinal("PriorTotalAmount")),
                NewTotalAmount = reader.GetDecimal(reader.GetOrdinal("NewTotalAmount")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                CorrectedBy = reader.GetInt32(reader.GetOrdinal("CorrectedBy")),
                CorrectedByName = reader.GetString(reader.GetOrdinal("CorrectedByName")),
                CorrectedAt = reader.GetDateTime(reader.GetOrdinal("CorrectedAt"))
            };
        }

        private static UnscheduledDeliveryAuthorization MapAuthorization(SqlDataReader reader)
        {
            return new UnscheduledDeliveryAuthorization
            {
                AuthorizationId = reader.GetInt64(reader.GetOrdinal("AuthorizationId")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                CategoryId = reader.IsDBNull(reader.GetOrdinal("CategoryId")) ? null : reader.GetInt32(reader.GetOrdinal("CategoryId")),
                CategoryName = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
                EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("EntryDate"))),
                AuthorizedShift = reader.GetString(reader.GetOrdinal("AuthorizedShift")),
                MaxQuantity = reader.IsDBNull(reader.GetOrdinal("MaxQuantity")) ? null : reader.GetDecimal(reader.GetOrdinal("MaxQuantity")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                AuthorizedBy = reader.GetInt32(reader.GetOrdinal("AuthorizedBy")),
                AuthorizedByName = reader.GetString(reader.GetOrdinal("AuthorizedByName")),
                AuthorizedAt = reader.GetDateTime(reader.GetOrdinal("AuthorizedAt")),
                IsRevoked = reader.GetBoolean(reader.GetOrdinal("IsRevoked")),
                RevokedBy = reader.IsDBNull(reader.GetOrdinal("RevokedBy")) ? null : reader.GetInt32(reader.GetOrdinal("RevokedBy")),
                RevokedAt = reader.IsDBNull(reader.GetOrdinal("RevokedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("RevokedAt")),
                RevokeReason = reader.IsDBNull(reader.GetOrdinal("RevokeReason")) ? null : reader.GetString(reader.GetOrdinal("RevokeReason"))
            };
        }

        #endregion
    }
}
