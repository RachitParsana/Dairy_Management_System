using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.DailyMilk;
using Dairy_Management_System.Models.DeliveryBoy;
using Dairy_Management_System.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Core application service implementing IDailyMilkService.
    /// Orchestrates daily runsheets, draft saves, shift submissions, unscheduled delivery authorizations,
    /// administrative corrections with audit logging, and effective-dated commission tracking.
    /// </summary>
    public class DailyMilkService : IDailyMilkService, IDailyMilkBatchAdminService
    {
        private readonly IDailyMilkRepository _dailyMilkRepository;
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<DailyMilkService> _logger;

        public DailyMilkService(
            IDailyMilkRepository dailyMilkRepository,
            ISqlConnectionFactory connectionFactory,
            ILogger<DailyMilkService> logger)
        {
            _dailyMilkRepository = dailyMilkRepository ?? throw new ArgumentNullException(nameof(dailyMilkRepository));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper: Resolve Delivery Boy by User ID

        private async Task<(int DeliveryBoyId, string FullName, int PrimaryRouteId)?> ResolveDeliveryBoyByUserIdAsync(
            int userId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT db.DeliveryBoyId, u.FullName, db.PrimaryRouteId
                FROM [dbo].[DeliveryBoys] db
                JOIN [dbo].[Users] u ON db.UserId = u.UserId
                WHERE db.UserId = @UserId AND db.IsActive = 1;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return (
                    reader.GetInt32(reader.GetOrdinal("DeliveryBoyId")),
                    reader.GetString(reader.GetOrdinal("FullName")),
                    reader.GetInt32(reader.GetOrdinal("PrimaryRouteId"))
                );
            }

            return null;
        }

        #endregion

        #region Runsheet & Continuous Scrolling Operations

        public async Task<DailyMilkRosterViewModel> GetDeliveryRosterAsync(
            int deliveryBoyUserId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            var deliveryBoy = await ResolveDeliveryBoyByUserIdAsync(deliveryBoyUserId, cancellationToken).ConfigureAwait(false);
            if (deliveryBoy == null)
            {
                return new DailyMilkRosterViewModel
                {
                    EntryDate = entryDate,
                    Shift = shift
                };
            }

            int deliveryBoyId = deliveryBoy.Value.DeliveryBoyId;
            var items = await _dailyMilkRepository.GetRosterAsync(deliveryBoyId, entryDate, shift, cancellationToken).ConfigureAwait(false);
            var batch = await _dailyMilkRepository.GetBatchAsync(deliveryBoyId, entryDate, shift, cancellationToken).ConfigureAwait(false);

            int completed = 0, pending = 0, notDelivered = 0, vacation = 0;
            decimal volume = 0, amount = 0;

            foreach (var item in items)
            {
                if (item.IsOnApprovedVacation) vacation++;
                else if (item.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) completed++;
                else if (item.Status.Equals("Not Delivered", StringComparison.OrdinalIgnoreCase)) notDelivered++;
                else pending++;

                volume += item.TotalLiquidLitres;
                amount += item.TotalAmount;
            }

            return new DailyMilkRosterViewModel
            {
                DeliveryBoyId = deliveryBoyId,
                DeliveryBoyName = deliveryBoy.Value.FullName,
                RouteId = deliveryBoy.Value.PrimaryRouteId,
                EntryDate = entryDate,
                Shift = shift,
                BatchId = batch?.BatchId,
                BatchStatus = batch?.Status ?? "New",
                SubmittedAt = batch?.SubmittedAt,
                TotalCustomers = items.Count,
                CompletedCount = completed,
                PendingCount = pending,
                NotDeliveredCount = notDelivered,
                VacationCount = vacation,
                TotalLiquidLitres = volume,
                TotalAmount = amount,
                Customers = new List<DailyMilkListItemViewModel>(items)
            };
        }

        public async Task<DailyMilkRosterViewModel> GetAdminDeliveryRosterAsync(
            int? routeId,
            int? deliveryBoyId,
            DateOnly entryDate,
            string shift,
            CancellationToken cancellationToken = default)
        {
            var items = await _dailyMilkRepository.GetAdminRosterAsync(routeId, deliveryBoyId, entryDate, shift, cancellationToken).ConfigureAwait(false);

            DailyMilkBatch? batch = null;
            if (deliveryBoyId.HasValue)
            {
                batch = await _dailyMilkRepository.GetBatchAsync(deliveryBoyId.Value, entryDate, shift, cancellationToken).ConfigureAwait(false);
            }

            int completed = 0, pending = 0, notDelivered = 0, vacation = 0;
            decimal volume = 0, amount = 0;

            foreach (var item in items)
            {
                if (item.IsOnApprovedVacation) vacation++;
                else if (item.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) completed++;
                else if (item.Status.Equals("Not Delivered", StringComparison.OrdinalIgnoreCase)) notDelivered++;
                else pending++;

                volume += item.TotalLiquidLitres;
                amount += item.TotalAmount;
            }

            return new DailyMilkRosterViewModel
            {
                DeliveryBoyId = deliveryBoyId ?? 0,
                RouteId = routeId ?? 0,
                EntryDate = entryDate,
                Shift = shift,
                BatchId = batch?.BatchId,
                BatchStatus = batch?.Status ?? "New",
                SubmittedAt = batch?.SubmittedAt,
                TotalCustomers = items.Count,
                CompletedCount = completed,
                PendingCount = pending,
                NotDeliveredCount = notDelivered,
                VacationCount = vacation,
                TotalLiquidLitres = volume,
                TotalAmount = amount,
                Customers = new List<DailyMilkListItemViewModel>(items)
            };
        }

        public async Task<DailyMilkEntryViewModel?> GetDeliveryDetailsAsync(
            long deliveryId,
            CancellationToken cancellationToken = default)
        {
            var delivery = await _dailyMilkRepository.GetDeliveryByIdAsync(deliveryId, cancellationToken).ConfigureAwait(false);
            if (delivery == null)
            {
                return null;
            }

            var categoryItems = new List<DailyMilkCategoryItemViewModel>();
            if (delivery.Items != null)
            {
                foreach (var item in delivery.Items)
                {
                    categoryItems.Add(new DailyMilkCategoryItemViewModel
                    {
                        CategoryId = item.CategoryId,
                        CategoryName = item.CategoryName ?? string.Empty,
                        ProductType = item.ProductTypeSnapshot,
                        UnitOfMeasure = item.UnitOfMeasureSnapshot,
                        PricingBasis = item.PricingBasisSnapshot,
                        DeliveredQuantity = item.PackagingQuantity,
                        BillingUnitEquivalent = item.BillingUnitEquivalentSnapshot,
                        LitreEquivalent = item.LitreEquivalentSnapshot,
                        LiquidVolumeLitres = item.LiquidVolumeLitres,
                        UnitPrice = item.UnitPriceSnapshot,
                        SubTotal = item.SubTotal
                    });
                }
            }

            var corrections = await _dailyMilkRepository.GetCorrectionHistoryAsync(deliveryId, cancellationToken).ConfigureAwait(false);

            return new DailyMilkEntryViewModel
            {
                DeliveryId = delivery.DeliveryId,
                BatchId = delivery.BatchId,
                CustomerId = delivery.CustomerId,
                DeliveryBoyId = delivery.DeliveryBoyId,
                EntryDate = delivery.EntryDate,
                Shift = delivery.Shift,
                Status = delivery.Status,
                VacationId = delivery.VacationId,
                NonDeliveryReason = delivery.NonDeliveryReason,
                CommissionRateSnapshot = delivery.CommissionRateSnapshot,
                TotalLiquidVolumeLitres = delivery.TotalLiquidVolumeLitres,
                EligibleLitreQuantity = delivery.EligibleLitreQuantity,
                TotalAmount = delivery.TotalAmount,
                IsCorrected = delivery.IsCorrected,
                LastCorrectedAt = delivery.LastCorrectedAt,
                CreatedAt = delivery.CreatedAt,
                UpdatedAt = delivery.UpdatedAt,
                Items = categoryItems,
                Corrections = new List<DailyMilkCorrectionRecord>(corrections)
            };
        }

        public async Task<DailyMilkContinuousScrollResult<DailyMilkListItemViewModel>> GetDeliveriesContinuousAsync(
            DailyMilkFilterCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(criteria);
            return await _dailyMilkRepository.GetDeliveriesContinuousAsync(criteria, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Draft & Shift Submission

        public async Task<DailyMilkOperationResult> SaveDraftAsync(
            DailyMilkSubmissionViewModel model,
            int deliveryBoyUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var deliveryBoy = await ResolveDeliveryBoyByUserIdAsync(deliveryBoyUserId, cancellationToken).ConfigureAwait(false);
            if (deliveryBoy == null)
            {
                return DailyMilkOperationResult.Failure("User is not an authorized, active delivery boy.");
            }

            int deliveryBoyId = deliveryBoy.Value.DeliveryBoyId;

            try
            {
                var batch = new DailyMilkBatch
                {
                    DeliveryBoyId = deliveryBoyId,
                    RouteId = model.RouteId.HasValue && model.RouteId.Value > 0 ? model.RouteId.Value : deliveryBoy.Value.PrimaryRouteId,
                    EntryDate = model.EntryDate,
                    Shift = model.Shift,
                    Status = "Draft",
                    CreatedBy = deliveryBoyUserId
                };

                var entries = MapEntriesFromSubmission(model, deliveryBoyId, deliveryBoyUserId);
                long batchId = await _dailyMilkRepository.SaveBatchAsync(batch, entries, cancellationToken).ConfigureAwait(false);

                return DailyMilkOperationResult.SuccessBatch(batchId, entries.Count, "Draft runsheet saved successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to save daily milk draft runsheet for DeliveryBoy {DeliveryBoyId}", deliveryBoyId);
                return DailyMilkOperationResult.Failure(MapSaveBatchSqlError(ex, model.EntryDate));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error saving daily milk draft runsheet for DeliveryBoy {DeliveryBoyId}", deliveryBoyId);
                return DailyMilkOperationResult.Failure("Unable to save draft runsheet. Please refresh and try again.");
            }
        }

        public async Task<DailyMilkOperationResult> SubmitDailyDeliveriesAsync(
            DailyMilkSubmissionViewModel model,
            int deliveryBoyUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            var deliveryBoy = await ResolveDeliveryBoyByUserIdAsync(deliveryBoyUserId, cancellationToken).ConfigureAwait(false);
            if (deliveryBoy == null)
            {
                return DailyMilkOperationResult.Failure("User is not an authorized, active delivery boy.");
            }

            int deliveryBoyId = deliveryBoy.Value.DeliveryBoyId;

            // Business rule: Verify commission rate exists before submitting (Zero Silent Fallback)
            var resolvedRate = await _dailyMilkRepository.ResolveCommissionRateAsync(deliveryBoyId, model.EntryDate, cancellationToken).ConfigureAwait(false);
            if (!resolvedRate.HasValue)
            {
                return DailyMilkOperationResult.Failure(
                    $"Cannot finalize shift: No verified commission rate configured for this delivery boy covering EntryDate {model.EntryDate:yyyy-MM-dd}. An administrator must explicitly configure an effective-dated commission rate tier covering this date before finalizing this shift.");
            }

            try
            {
                var batch = new DailyMilkBatch
                {
                    DeliveryBoyId = deliveryBoyId,
                    RouteId = model.RouteId.HasValue && model.RouteId.Value > 0 ? model.RouteId.Value : deliveryBoy.Value.PrimaryRouteId,
                    EntryDate = model.EntryDate,
                    Shift = model.Shift,
                    Status = "Submitted",
                    CreatedBy = deliveryBoyUserId
                };

                var entries = MapEntriesFromSubmission(model, deliveryBoyId, deliveryBoyUserId);
                long batchId = await _dailyMilkRepository.SaveBatchAsync(batch, entries, cancellationToken).ConfigureAwait(false);

                return DailyMilkOperationResult.SuccessBatch(batchId, entries.Count, "Shift deliveries finalized and submitted successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to submit daily deliveries for DeliveryBoy {DeliveryBoyId}", deliveryBoyId);
                return DailyMilkOperationResult.Failure(MapSaveBatchSqlError(ex, model.EntryDate));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error submitting daily deliveries for DeliveryBoy {DeliveryBoyId}", deliveryBoyId);
                return DailyMilkOperationResult.Failure("Unable to finalize shift submission. Please refresh and try again.");
            }
        }

        private static string MapSaveBatchSqlError(SqlException ex, DateOnly entryDate) => ex.Number switch
        {
            50020 => "Shift batch has already been finalized and submitted. No further modifications permitted.",
            50021 => "One or more customers are not assigned to this delivery boy on this date.",
            50022 => "Cannot submit delivery: One or more customers are on approved vacation for this shift and date.",
            50025 => "Unscheduled delivery on off-schedule date is not authorized by an Administrator for this shift.",
            50035 => $"Cannot finalize shift: No verified commission rate configured for this delivery boy covering EntryDate {entryDate:yyyy-MM-dd}. An administrator must explicitly configure an effective-dated commission rate tier covering this date before finalizing this shift.",
            _ => ex.Message
        };

        private static List<DailyMilkEntry> MapEntriesFromSubmission(
            DailyMilkSubmissionViewModel model,
            int deliveryBoyId,
            int userId)
        {
            var entries = new List<DailyMilkEntry>();

            foreach (var item in model.CustomerEntries)
            {
                var entry = new DailyMilkEntry
                {
                    CustomerId = item.CustomerId,
                    DeliveryBoyId = deliveryBoyId,
                    EntryDate = model.EntryDate,
                    Shift = model.Shift,
                    Status = item.Status,
                    NonDeliveryReason = item.NonDeliveryReason,
                    IsUnscheduledDelivery = item.IsUnscheduledDelivery,
                    CreatedBy = userId
                };

                if (item.CategoryQuantities != null && item.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    var lines = new List<DailyMilkCategoryEntry>();
                    foreach (var cat in item.CategoryQuantities)
                    {
                        lines.Add(new DailyMilkCategoryEntry
                        {
                            CategoryId = cat.CategoryId,
                            PackagingQuantity = cat.PackagingQuantity
                        });
                    }
                    entry.Items = lines;
                }

                entries.Add(entry);
            }

            return entries;
        }

        #endregion

        #region Administrative Corrections & Unscheduled Authorizations

        public async Task<DailyMilkOperationResult> CorrectDeliveryAsync(
            DailyMilkCorrectionViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (string.IsNullOrWhiteSpace(model.CorrectionReason) || model.CorrectionReason.Trim().Length < 5)
            {
                return DailyMilkOperationResult.Failure("Correction reason must be at least 5 characters long.");
            }

            var existingDelivery = await _dailyMilkRepository.GetDeliveryByIdAsync(model.DeliveryId, cancellationToken).ConfigureAwait(false);
            if (existingDelivery == null)
            {
                return DailyMilkOperationResult.Failure($"Delivery record with ID {model.DeliveryId} does not exist.");
            }

            bool isPublished = await _dailyMilkRepository.IsDeliveryDateBilledAndPublishedAsync(existingDelivery.CustomerId, existingDelivery.EntryDate, cancellationToken).ConfigureAwait(false);
            if (isPublished)
            {
                return DailyMilkOperationResult.Failure("Cannot modify daily delivery for a published billing month. Use Bill Revision.");
            }

            try
            {
                var updatedItems = new List<DailyMilkCategoryEntry>();
                if (model.CategoryQuantities != null && model.NewStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in model.CategoryQuantities)
                    {
                        updatedItems.Add(new DailyMilkCategoryEntry
                        {
                            CategoryId = item.CategoryId,
                            PackagingQuantity = item.PackagingQuantity
                        });
                    }
                }

                await _dailyMilkRepository.CorrectDeliveryEntryAsync(
                    model.DeliveryId,
                    model.NewStatus,
                    model.NonDeliveryReason,
                    updatedItems,
                    model.CorrectionReason.Trim(),
                    adminUserId,
                    cancellationToken).ConfigureAwait(false);

                return DailyMilkOperationResult.SuccessDelivery(model.DeliveryId, "Delivery record corrected and audit ledger updated successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to correct delivery record {DeliveryId}", model.DeliveryId);
                if (ex.Number == 50030)
                {
                    return DailyMilkOperationResult.Failure("Cannot modify daily delivery for a published billing month. Use Bill Revision.");
                }
                return DailyMilkOperationResult.Failure(ex.Message);
            }
        }

        public async Task<DailyMilkOperationResult> AuthorizeUnscheduledDeliveryAsync(
            CreateUnscheduledAuthorizationViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            {
                return DailyMilkOperationResult.Failure("Authorization reason must be at least 5 characters long.");
            }

            try
            {
                var authorization = new UnscheduledDeliveryAuthorization
                {
                    CustomerId = model.CustomerId,
                    CategoryId = model.CategoryId,
                    EntryDate = model.EntryDate,
                    AuthorizedShift = model.Shift,
                    MaxQuantity = model.MaxQuantity,
                    Reason = model.Reason.Trim(),
                    AuthorizedBy = adminUserId
                };

                long authId = await _dailyMilkRepository.CreateUnscheduledAuthorizationAsync(authorization, cancellationToken).ConfigureAwait(false);
                return DailyMilkOperationResult.SuccessDelivery(authId, "Unscheduled delivery authorization granted successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to authorize unscheduled delivery for Customer {CustomerId}", model.CustomerId);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
        }

        public async Task<DailyMilkOperationResult> RevokeUnscheduledAuthorizationAsync(
            long authorizationId,
            string revokeReason,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(revokeReason) || revokeReason.Trim().Length < 5)
            {
                return DailyMilkOperationResult.Failure("Revocation reason must be at least 5 characters long.");
            }

            try
            {
                await _dailyMilkRepository.RevokeUnscheduledAuthorizationAsync(authorizationId, revokeReason.Trim(), adminUserId, cancellationToken).ConfigureAwait(false);
                return DailyMilkOperationResult.SuccessDelivery(authorizationId, "Unscheduled delivery authorization revoked successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to revoke unscheduled authorization {AuthorizationId}", authorizationId);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
        }

        public async Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetCorrectionHistoryAsync(
            long deliveryId,
            CancellationToken cancellationToken = default)
        {
            return await _dailyMilkRepository.GetCorrectionHistoryAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DailyMilkCorrectionRecord>> GetRecentCorrectionsAsync(
            int count = 50,
            CancellationToken cancellationToken = default)
        {
            return await _dailyMilkRepository.GetRecentCorrectionsAsync(count, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Effective-Dated Commission Rate Management

        public async Task<IReadOnlyList<DeliveryBoyCommissionRate>> GetCommissionRateHistoryAsync(
            int deliveryBoyId,
            CancellationToken cancellationToken = default)
        {
            return await _dailyMilkRepository.GetCommissionRateHistoryAsync(deliveryBoyId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DailyMilkOperationResult> InitializeCommissionRateAsync(
            InitializeDeliveryBoyCommissionRateViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (string.IsNullOrWhiteSpace(model.Justification) || model.Justification.Trim().Length < 5)
            {
                return DailyMilkOperationResult.Failure("Justification must be at least 5 characters long.");
            }

            try
            {
                var rate = new DeliveryBoyCommissionRate
                {
                    DeliveryBoyId = model.DeliveryBoyId,
                    CommissionRate = model.CommissionRate,
                    EffectiveFrom = model.EffectiveFrom,
                    EffectiveTo = null,
                    Notes = model.Justification.Trim(),
                    CreatedBy = adminUserId
                };

                int rateId = await _dailyMilkRepository.CreateCommissionRateAsync(rate, cancellationToken).ConfigureAwait(false);
                return DailyMilkOperationResult.SuccessDelivery(rateId, "Baseline commission rate tier initialized successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to initialize commission rate for DeliveryBoy {DeliveryBoyId}", model.DeliveryBoyId);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
        }

        public async Task<DailyMilkOperationResult> AddCommissionRateAsync(
            CreateDeliveryBoyCommissionRateViewModel model,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            try
            {
                var rate = new DeliveryBoyCommissionRate
                {
                    DeliveryBoyId = model.DeliveryBoyId,
                    CommissionRate = model.CommissionRate,
                    EffectiveFrom = model.EffectiveFrom,
                    EffectiveTo = model.EffectiveTo,
                    Notes = model.Notes?.Trim(),
                    CreatedBy = adminUserId
                };

                int rateId = await _dailyMilkRepository.CreateCommissionRateAsync(rate, cancellationToken).ConfigureAwait(false);
                return DailyMilkOperationResult.SuccessDelivery(rateId, "Commission rate tier created successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Failed to add commission rate tier for DeliveryBoy {DeliveryBoyId}", model.DeliveryBoyId);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
        }

        #endregion

        #region Pricing, Commission & Summary Inquiries

        public async Task<decimal> GetEffectiveUnitPriceAsync(
            int customerId,
            int categoryId,
            DateOnly entryDate,
            CancellationToken cancellationToken = default)
        {
            var price = await _dailyMilkRepository.ResolveUnitPriceAsync(customerId, categoryId, entryDate, cancellationToken).ConfigureAwait(false);
            if (!price.HasValue || price.Value <= 0)
            {
                throw new InvalidOperationException($"No active or positive price configured for Category ID {categoryId} on {entryDate:yyyy-MM-dd}.");
            }

            return price.Value;
        }

        public async Task<DailyMilkSummaryViewModel> GetDailySummaryAsync(
            DateOnly entryDate,
            string shift,
            int? deliveryBoyId = null,
            CancellationToken cancellationToken = default)
        {
            return await _dailyMilkRepository.GetDailyShiftSummaryAsync(entryDate, shift, deliveryBoyId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<DeliveryBoyCustomerRosterItemViewModel>> GetAssignedCustomersRosterAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var dbInfo = await ResolveDeliveryBoyByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (!dbInfo.HasValue)
            {
                return Array.Empty<DeliveryBoyCustomerRosterItemViewModel>();
            }

            var list = new List<DeliveryBoyCustomerRosterItemViewModel>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_DeliveryBoy_GetCustomerRoster", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add("@DeliveryBoyId", SqlDbType.Int).Value = dbInfo.Value.DeliveryBoyId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new DeliveryBoyCustomerRosterItemViewModel
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("CustomerCode")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    Phone = reader.GetString(reader.GetOrdinal("Phone")),
                    Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? string.Empty : reader.GetString(reader.GetOrdinal("Address")),
                    Area = reader.IsDBNull(reader.GetOrdinal("Area")) ? string.Empty : reader.GetString(reader.GetOrdinal("Area")),
                    Landmark = reader.IsDBNull(reader.GetOrdinal("Landmark")) ? string.Empty : reader.GetString(reader.GetOrdinal("Landmark")),
                    DeliverySequence = reader.GetInt32(reader.GetOrdinal("DeliverySequence")),
                    SubscriptionId = reader.IsDBNull(reader.GetOrdinal("SubscriptionId")) ? null : reader.GetInt32(reader.GetOrdinal("SubscriptionId")),
                    CategoryName = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? string.Empty : reader.GetString(reader.GetOrdinal("CategoryName")),
                    Unit = reader.IsDBNull(reader.GetOrdinal("Unit")) ? string.Empty : reader.GetString(reader.GetOrdinal("Unit")),
                    DailyQuantity = reader.IsDBNull(reader.GetOrdinal("DailyQuantity")) ? 0m : reader.GetDecimal(reader.GetOrdinal("DailyQuantity")),
                    ShiftPreference = reader.IsDBNull(reader.GetOrdinal("ShiftPreference")) ? string.Empty : reader.GetString(reader.GetOrdinal("ShiftPreference")),
                    IsOnVacationToday = !reader.IsDBNull(reader.GetOrdinal("IsOnVacationToday")) && Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsOnVacationToday")))
                });
            }

            return list;
        }

        #endregion

        #region Admin Batch Lock & Unlock Management

        public async Task<IReadOnlyList<DailyMilkBatchAdminItemDto>> GetBatchesForAdminAsync(
            DateOnly? entryDate,
            string? shift,
            int? deliveryBoyId,
            string? status = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            if (_dailyMilkRepository is not IDailyMilkBatchAdminRepository batchRepo)
            {
                return Array.Empty<DailyMilkBatchAdminItemDto>();
            }

            return await batchRepo.GetBatchesForAdminAsync(entryDate, shift, deliveryBoyId, status, limit, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<DailyMilkOperationResult> UnlockBatchAsync(
            long batchId,
            string unlockReason,
            int adminUserId,
            CancellationToken cancellationToken = default)
        {
            if (batchId <= 0)
            {
                return DailyMilkOperationResult.Failure("Invalid delivery batch identifier.");
            }

            if (string.IsNullOrWhiteSpace(unlockReason) || unlockReason.Trim().Length < 5)
            {
                return DailyMilkOperationResult.Failure("Unlock justification must be at least 5 characters long.");
            }

            if (_dailyMilkRepository is not IDailyMilkBatchAdminRepository batchRepo)
            {
                return DailyMilkOperationResult.Failure("Batch unlock repository is unavailable.");
            }

            try
            {
                await batchRepo.UnlockBatchAsync(batchId, unlockReason.Trim(), adminUserId, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("Admin {AdminUserId} unlocked DailyDeliveryBatch {BatchId}.", adminUserId, batchId);
                return DailyMilkOperationResult.SuccessBatch(batchId, 1, "Shift runsheet unlocked cleanly back to Draft. The Delivery Boy can now modify and re-submit entries.");
            }
            catch (SqlException ex) when (ex.Number is >= 50041 and <= 50046)
            {
                _logger.LogWarning(ex, "Blocked attempt by Admin {AdminUserId} to unlock BatchId {BatchId}: {Message}", adminUserId, batchId, ex.Message);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL failure unlocking BatchId {BatchId} by Admin {AdminUserId}.", batchId, adminUserId);
                return DailyMilkOperationResult.Failure(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected failure unlocking BatchId {BatchId} by Admin {AdminUserId}.", batchId, adminUserId);
                return DailyMilkOperationResult.Failure("An unexpected error occurred while unlocking the shift batch.");
            }
        }

        #endregion
    }
}
