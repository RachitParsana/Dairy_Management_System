using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Billing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Production-grade SQL Server ADO.NET repository implementing IBillingRepository.
    /// Operates exclusively through compiled static SQL stored procedures and queries
    /// the append-only dbo.PaymentAllocationEvents ledger. Never accesses dbo.PaymentAllocations.
    /// </summary>
    public class BillingRepository : IBillingRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<BillingRepository> _logger;

        public BillingRepository(ISqlConnectionFactory connectionFactory, ILogger<BillingRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Deadlock Retry Policy

        /// <summary>
        /// Executes an asynchronous operation with full unit-of-work retry upon SQL Server error 1205 (deadlock).
        /// Retries up to 3 times with exponential backoff and jitter. Never catches business exceptions.
        /// </summary>
        private static async Task<T> ExecuteWithDeadlockRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
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

        #endregion

        #region Invoice Operations

        public async Task<long> GenerateCustomerBillAsync(int customerId, int month, int year, int adminUserId, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_MonthlyBilling_GenerateCustomerBill", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                command.Parameters.Add("@BillingMonth", SqlDbType.TinyInt).Value = (byte)month;
                command.Parameters.Add("@BillingYear", SqlDbType.SmallInt).Value = (short)year;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var outputParam = new SqlParameter("@BillId", SqlDbType.BigInt)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outputParam);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                return (outputParam.Value != DBNull.Value) ? (long)outputParam.Value : 0L;
            }).ConfigureAwait(false);
        }

        public async Task<MonthlyBill?> GetBillByIdAsync(long billId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_MonthlyBilling_GetBillDetails", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@BillId", SqlDbType.BigInt).Value = billId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // Result Set 1: Bill Header & Customer Information
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var bill = MapMonthlyBillHeader(reader);

            // Result Set 2: Itemized Lines
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    bill.Items.Add(MapMonthlyBillItem(reader));
                }
            }

            // Result Set 3: Revision History
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    bill.Revisions.Add(MapMonthlyBillRevision(reader));
                }
            }

            // Result Set 4: Allocation Events (From dbo.PaymentAllocationEvents)
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    bill.AllocationEvents.Add(MapPaymentAllocationEvent(reader));
                }
            }

            return bill;
        }

        public async Task<IReadOnlyList<MonthlyBill>> GetBillsAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            var list = new List<MonthlyBill>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_MonthlyBilling_GetBillsContinuous", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            AddBillsFilterParameters(command, criteria);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // Skip Result Set 1 (TotalCount) to get Result Set 2 (Bills)
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(MapMonthlyBillSummaryRow(reader));
                }
            }

            return list;
        }

        public async Task<int> GetBillsCountAsync(MonthlyBillFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_MonthlyBilling_GetBillsContinuous", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            AddBillsFilterParameters(command, criteria);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return reader.GetInt32(0);
            }

            return 0;
        }

        public async Task<bool> PublishBillAsync(long billId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_MonthlyBilling_PublishBill", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@BillId", SqlDbType.BigInt).Value = billId;
                command.Parameters.Add("@GraceDays", SqlDbType.Int).Value = (object?)graceDays ?? DBNull.Value;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        public async Task<int> BulkPublishBillsAsync(int month, int year, int? routeId, int? graceDays, int adminUserId, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_MonthlyBilling_BulkPublish", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                command.Parameters.Add("@BillingMonth", SqlDbType.TinyInt).Value = (byte)month;
                command.Parameters.Add("@BillingYear", SqlDbType.SmallInt).Value = (short)year;
                command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)routeId ?? DBNull.Value;
                command.Parameters.Add("@GraceDays", SqlDbType.Int).Value = (object?)graceDays ?? DBNull.Value;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var outputParam = new SqlParameter("@PublishedCount", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outputParam);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                return (outputParam.Value != DBNull.Value) ? (int)outputParam.Value : 0;
            }).ConfigureAwait(false);
        }

        public async Task<bool> ReviseBillAsync(ReviseBillViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_MonthlyBilling_ReviseBill", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@BillId", SqlDbType.BigInt).Value = model.BillId;
                command.Parameters.Add("@ExpectedVersion", SqlDbType.Int).Value = model.ExpectedVersion;
                var pAdjustments = command.Parameters.Add("@Adjustments", SqlDbType.Decimal);
                pAdjustments.Precision = 18;
                pAdjustments.Scale = 2;
                pAdjustments.Value = model.Adjustments;
                command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = model.Reason;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var outputParam = new SqlParameter("@NewRevisionId", SqlDbType.BigInt)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outputParam);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        #endregion

        #region Bulk Operations & Eligibility

        public async Task<IReadOnlyList<CustomerBillingEligibilityModel>> GetEligibleCustomersForBillingAsync(int month, int year, int? routeId, CancellationToken cancellationToken = default)
        {
            var list = new List<CustomerBillingEligibilityModel>();
            var startDate = new DateOnly(year, month, 1);
            var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            const string sql = @"
                SELECT 
                    c.CustomerId,
                    c.FullName,
                    c.Phone,
                    c.RouteId,
                    r.RouteName,
                    c.IsActive,
                    ISNULL(deliv.CompletedDeliveriesCount, 0) AS CompletedDeliveriesCount,
                    ISNULL(deliv.DeliveredLitres, 0.00) AS DeliveredLitres,
                    CASE WHEN b.BillId IS NOT NULL THEN 1 ELSE 0 END AS HasExistingBill,
                    b.BillId AS ExistingBillId,
                    b.PaymentStatus AS ExistingBillStatus
                FROM dbo.Customers c
                LEFT JOIN dbo.Routes r ON c.RouteId = r.RouteId
                LEFT JOIN (
                    SELECT 
                        d.CustomerId,
                        COUNT(DISTINCT d.DeliveryId) AS CompletedDeliveriesCount,
                        SUM(i.PackagingQuantity * cat.LitreEquivalent) AS DeliveredLitres
                    FROM dbo.DailyMilkDeliveries d
                    JOIN dbo.DailyMilkDeliveryItems i ON d.DeliveryId = i.DeliveryId
                    JOIN dbo.MilkCategories cat ON i.CategoryId = cat.CategoryId
                    WHERE d.EntryDate >= @StartDate AND d.EntryDate <= @EndDate
                      AND d.Status = 'Completed'
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.CustomerVacations v
                          WHERE v.CustomerId = d.CustomerId
                            AND v.Status = 'Approved'
                            AND d.EntryDate >= v.StartDate AND d.EntryDate <= v.EndDate
                      )
                    GROUP BY d.CustomerId
                ) deliv ON c.CustomerId = deliv.CustomerId
                LEFT JOIN dbo.MonthlyBills b ON c.CustomerId = b.CustomerId 
                    AND b.BillingMonth = @Month AND b.BillingYear = @Year
                WHERE c.IsActive = 1
                  AND (@RouteId IS NULL OR c.RouteId = @RouteId)
                ORDER BY c.FullName ASC;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };

            command.Parameters.Add("@Month", SqlDbType.TinyInt).Value = (byte)month;
            command.Parameters.Add("@Year", SqlDbType.SmallInt).Value = (short)year;
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)routeId ?? DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerBillingEligibilityModel
                {
                    CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                    FullName = reader.GetString(reader.GetOrdinal("FullName")),
                    Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                    RouteId = reader.IsDBNull(reader.GetOrdinal("RouteId")) ? null : reader.GetInt32(reader.GetOrdinal("RouteId")),
                    RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    CompletedDeliveriesCount = reader.GetInt32(reader.GetOrdinal("CompletedDeliveriesCount")),
                    DeliveredLitres = reader.GetDecimal(reader.GetOrdinal("DeliveredLitres")),
                    HasExistingBill = reader.GetInt32(reader.GetOrdinal("HasExistingBill")) == 1,
                    ExistingBillId = reader.IsDBNull(reader.GetOrdinal("ExistingBillId")) ? null : reader.GetInt64(reader.GetOrdinal("ExistingBillId")),
                    ExistingBillStatus = reader.IsDBNull(reader.GetOrdinal("ExistingBillStatus")) ? null : reader.GetString(reader.GetOrdinal("ExistingBillStatus"))
                });
            }

            return list;
        }

        public async Task<BulkBillingResultModel> GenerateBulkMonthlyBillsAsync(int month, int year, int? routeId, int adminUserId, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_MonthlyBilling_GenerateBulk", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 300 // allow 5 minutes for large bulk batches
                };

                command.Parameters.Add("@BillingMonth", SqlDbType.TinyInt).Value = (byte)month;
                command.Parameters.Add("@BillingYear", SqlDbType.SmallInt).Value = (short)year;
                command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)routeId ?? DBNull.Value;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var pTotalEligible = new SqlParameter("@TotalEligible", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pTotalSucceeded = new SqlParameter("@TotalSucceeded", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pTotalSkipped = new SqlParameter("@TotalSkipped", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pTotalFailed = new SqlParameter("@TotalFailed", SqlDbType.Int) { Direction = ParameterDirection.Output };

                command.Parameters.Add(pTotalEligible);
                command.Parameters.Add(pTotalSucceeded);
                command.Parameters.Add(pTotalSkipped);
                command.Parameters.Add(pTotalFailed);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                return new BulkBillingResultModel
                {
                    BillingMonth = (byte)month,
                    BillingYear = (short)year,
                    TotalEligible = (pTotalEligible.Value != DBNull.Value) ? (int)pTotalEligible.Value : 0,
                    TotalSucceeded = (pTotalSucceeded.Value != DBNull.Value) ? (int)pTotalSucceeded.Value : 0,
                    TotalSkipped = (pTotalSkipped.Value != DBNull.Value) ? (int)pTotalSkipped.Value : 0,
                    TotalFailed = (pTotalFailed.Value != DBNull.Value) ? (int)pTotalFailed.Value : 0
                };
            }).ConfigureAwait(false);
        }

        #endregion

        #region Append-Only Payment Operations (100% on dbo.PaymentAllocationEvents)

        public async Task<long> RecordAndAllocateSinglePaymentAsync(RecordSinglePaymentViewModel model, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Payment_RecordAndAllocateSingle", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = model.CustomerId;
                var pAmount = command.Parameters.Add("@Amount", SqlDbType.Decimal);
                pAmount.Precision = 18;
                pAmount.Scale = 2;
                pAmount.Value = model.Amount;
                command.Parameters.Add("@PaymentMode", SqlDbType.NVarChar, 20).Value = model.PaymentMode;
                command.Parameters.Add("@ReferenceNumber", SqlDbType.NVarChar, 100).Value = (object?)model.ReferenceNumber ?? DBNull.Value;
                command.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value = (object?)model.Notes ?? DBNull.Value;
                command.Parameters.Add("@SelectedBillId", SqlDbType.BigInt).Value = model.TargetBillId;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var pPaymentId = new SqlParameter("@NewPaymentId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
                var pAllocationEventId = new SqlParameter("@NewAllocationEventId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };

                command.Parameters.Add(pPaymentId);
                command.Parameters.Add(pAllocationEventId);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                return (pPaymentId.Value != DBNull.Value) ? (long)pPaymentId.Value : 0L;
            }).ConfigureAwait(false);
        }

        public async Task<bool> AllocateAdvanceCreditSingleAsync(long paymentId, long targetBillId, decimal amount, string? reason, int adminUserId, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Payment_AllocateAdvanceCreditSingle", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@PaymentId", SqlDbType.BigInt).Value = paymentId;
                command.Parameters.Add("@SelectedBillId", SqlDbType.BigInt).Value = targetBillId;
                var pAllocatedAmount = command.Parameters.Add("@AllocatedAmount", SqlDbType.Decimal);
                pAllocatedAmount.Precision = 18;
                pAllocatedAmount.Scale = 2;
                pAllocatedAmount.Value = amount;
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                var pAllocationEventId = new SqlParameter("@NewAllocationEventId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
                command.Parameters.Add(pAllocationEventId);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        public async Task<bool> VoidPaymentAsync(long paymentId, string voidReason, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(voidReason) || voidReason.Trim().Length < 10)
                throw new ArgumentException("Void justification reason must be at least 10 characters.", nameof(voidReason));

            return await ExecuteWithDeadlockRetryAsync(async () =>
            {
                await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new SqlCommand("dbo.usp_Payment_Void", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };

                command.Parameters.Add("@PaymentId", SqlDbType.BigInt).Value = paymentId;
                command.Parameters.Add("@VoidReason", SqlDbType.NVarChar, 500).Value = voidReason.Trim();
                command.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        public async Task<Payment?> GetPaymentByIdAsync(long paymentId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    p.PaymentId,
                    p.ReceiptNumber,
                    p.CustomerId,
                    c.FullName AS CustomerName,
                    c.Phone AS CustomerPhone,
                    p.PaymentDate,
                    p.Amount,
                    p.PaymentMode,
                    p.ReferenceNumber,
                    p.Notes,
                    p.IsVoided,
                    p.VoidedAt,
                    p.VoidReason,
                    p.ReceivedBy,
                    u.FullName AS ReceivedByUserName,
                    p.CreatedAt,
                    ISNULL((
                        SELECT SUM(pae.SignedAmount)
                        FROM dbo.PaymentAllocationEvents pae
                        WHERE pae.PaymentId = p.PaymentId
                    ), 0.00) AS AllocatedAmount
                FROM dbo.Payments p
                JOIN dbo.Customers c ON p.CustomerId = c.CustomerId
                JOIN dbo.Users u ON p.ReceivedBy = u.UserId
                WHERE p.PaymentId = @PaymentId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            command.Parameters.Add("@PaymentId", SqlDbType.BigInt).Value = paymentId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return MapPaymentRecord(reader);
        }

        public async Task<IReadOnlyList<Payment>> GetPaymentsAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            var list = new List<Payment>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Payment_GetHistory", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            AddPaymentsFilterParameters(command, criteria);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // Skip Result Set 1 (TotalCount) to get Result Set 2 (Payments)
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    list.Add(MapPaymentRecord(reader));
                }
            }

            return list;
        }

        public async Task<int> GetPaymentsCountAsync(PaymentFilterCriteria criteria, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Payment_GetHistory", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            AddPaymentsFilterParameters(command, criteria);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return reader.GetInt32(0);
            }

            return 0;
        }

        public async Task<IReadOnlyList<PaymentAllocationEvent>> GetAllocationEventsByBillIdAsync(long billId, CancellationToken cancellationToken = default)
        {
            var list = new List<PaymentAllocationEvent>();

            const string sql = @"
                SELECT 
                    pae.AllocationEventId,
                    pae.PaymentId,
                    p.ReceiptNumber,
                    pae.BillId,
                    b.BillNumber,
                    pae.EventType,
                    pae.OriginalAllocationEventId,
                    pae.Amount,
                    pae.Direction,
                    pae.Reason,
                    pae.RelatedRevisionId,
                    pae.LegacyAllocationId,
                    pae.PerformedBy,
                    u.FullName AS PerformedByUserName,
                    pae.PerformedAt
                FROM dbo.PaymentAllocationEvents pae
                JOIN dbo.Payments p ON pae.PaymentId = p.PaymentId
                JOIN dbo.MonthlyBills b ON pae.BillId = b.BillId
                JOIN dbo.Users u ON pae.PerformedBy = u.UserId
                WHERE pae.BillId = @BillId
                ORDER BY pae.AllocationEventId ASC;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            command.Parameters.Add("@BillId", SqlDbType.BigInt).Value = billId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(MapPaymentAllocationEvent(reader));
            }

            return list;
        }

        #endregion

        #region Customer Ledger & Balances (Authoritative Model)

        public async Task<IReadOnlyList<CustomerOutstandingBillItemViewModel>> GetCustomerOutstandingBillsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            var list = new List<CustomerOutstandingBillItemViewModel>();

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Payment_GetCustomerOutstandingBills", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(new CustomerOutstandingBillItemViewModel
                {
                    BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                    BillNumber = reader.GetString(reader.GetOrdinal("BillNumber")),
                    BillingMonth = reader.GetByte(reader.GetOrdinal("BillingMonth")),
                    BillingYear = (short)reader.GetInt16(reader.GetOrdinal("BillingYear")),
                    PeriodStartDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodStartDate"))),
                    PeriodEndDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodEndDate"))),
                    CurrentMonthInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthInvoiceAmount")),
                    PaidAmount = reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                    BalanceAmount = reader.GetDecimal(reader.GetOrdinal("BalanceAmount")),
                    PaymentDueDate = reader.IsDBNull(reader.GetOrdinal("PaymentDueDate"))
                        ? null
                        : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDueDate"))),
                    PaymentStatus = reader.GetString(reader.GetOrdinal("PaymentStatus"))
                });
            }

            return list;
        }

        public async Task<CustomerBalanceSummaryModel> GetCustomerBalanceSummaryAsync(int customerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    c.CustomerId,
                    c.FullName AS CustomerName,
                    c.Phone AS CustomerPhone,
                    -- Total outstanding dues from published bills (receivables)
                    ISNULL((
                        SELECT SUM(b.BalanceAmount)
                        FROM dbo.MonthlyBills b
                        WHERE b.CustomerId = c.CustomerId AND b.IsPublished = 1
                    ), 0.00) AS TotalOutstandingDues,
                    -- Total available advance credit on active non-voided payment receipts
                    ISNULL((
                        SELECT SUM(p.Amount - ISNULL(alloc.Allocated, 0.00))
                        FROM dbo.Payments p
                        OUTER APPLY (
                            SELECT SUM(pae.SignedAmount) AS Allocated
                            FROM dbo.PaymentAllocationEvents pae
                            WHERE pae.PaymentId = p.PaymentId
                        ) alloc
                        WHERE p.CustomerId = c.CustomerId AND p.IsVoided = 0
                    ), 0.00) AS TotalAvailableAdvanceCredit
                FROM dbo.Customers c
                WHERE c.CustomerId = @CustomerId;";

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new CustomerBalanceSummaryModel { CustomerId = customerId };
            }

            return new CustomerBalanceSummaryModel
            {
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
                CustomerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? null : reader.GetString(reader.GetOrdinal("CustomerPhone")),
                TotalOutstandingDues = reader.GetDecimal(reader.GetOrdinal("TotalOutstandingDues")),
                TotalAvailableAdvanceCredit = reader.GetDecimal(reader.GetOrdinal("TotalAvailableAdvanceCredit"))
            };
        }

        #endregion

        #region Reports

        public async Task<BillingSummaryReportModel> GetMonthlyBillingSummaryReportAsync(int month, int year, CancellationToken cancellationToken = default)
        {
            var report = new BillingSummaryReportModel
            {
                BillingMonth = (byte)month,
                BillingYear = (short)year
            };

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_BillingReports_GetMonthlySummary", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };

            command.Parameters.Add("@Month", SqlDbType.TinyInt).Value = (byte)month;
            command.Parameters.Add("@Year", SqlDbType.SmallInt).Value = (short)year;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            // Result Set 1: High-level metrics
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                report.TotalVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidMilkVolumeLitres"));
                report.TotalBilledAmount = reader.GetDecimal(reader.GetOrdinal("TotalCurrentMonthMilkAmount"));
                report.TotalAdjustments = reader.GetDecimal(reader.GetOrdinal("TotalAdjustments"));
                report.TotalInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("TotalCurrentMonthInvoiceAmount"));
                report.TotalCollections = reader.GetDecimal(reader.GetOrdinal("TotalPaidAmount"));
                report.TotalOutstanding = reader.GetDecimal(reader.GetOrdinal("TotalOutstandingBalance"));
                report.TotalBillsCount = reader.GetInt32(reader.GetOrdinal("TotalBillsCount"));
                report.PaidBillsCount = reader.GetInt32(reader.GetOrdinal("PaidBillsCount"));
                report.PartiallyPaidBillsCount = reader.GetInt32(reader.GetOrdinal("PartiallyPaidBillsCount"));
                report.UnpaidBillsCount = reader.GetInt32(reader.GetOrdinal("UnpaidBillsCount"));
                report.OverdueBillsCount = reader.GetInt32(reader.GetOrdinal("OverdueBillsCount"));
            }

            // Result Set 2: Route Breakdowns
            if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    report.RouteBreakdowns.Add(new BillingRouteSummaryModel
                    {
                        RouteId = reader.GetInt32(reader.GetOrdinal("RouteId")),
                        RouteName = reader.GetString(reader.GetOrdinal("RouteName")),
                        TotalCustomers = reader.GetInt32(reader.GetOrdinal("TotalCustomers")),
                        TotalBilledAmount = reader.GetDecimal(reader.GetOrdinal("BilledAmount")),
                        TotalCollectedAmount = reader.GetDecimal(reader.GetOrdinal("CollectedAmount")),
                        TotalOutstandingAmount = reader.GetDecimal(reader.GetOrdinal("OutstandingAmount"))
                    });
                }
            }

            return report;
        }

        #endregion

        #region Private Mapping Helpers

        private static void AddBillsFilterParameters(SqlCommand command, MonthlyBillFilterCriteria criteria)
        {
            command.Parameters.Add("@BillingMonth", SqlDbType.TinyInt).Value = (object?)criteria.BillingMonth ?? DBNull.Value;
            command.Parameters.Add("@BillingYear", SqlDbType.SmallInt).Value = (object?)criteria.BillingYear ?? DBNull.Value;
            command.Parameters.Add("@RouteId", SqlDbType.Int).Value = (object?)criteria.RouteId ?? DBNull.Value;
            command.Parameters.Add("@PaymentStatus", SqlDbType.NVarChar, 20).Value = (object?)criteria.PaymentStatus ?? DBNull.Value;
            command.Parameters.Add("@SearchText", SqlDbType.NVarChar, 100).Value = (object?)criteria.SearchText ?? DBNull.Value;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = criteria.Offset;
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = criteria.Limit;
        }

        private static void AddPaymentsFilterParameters(SqlCommand command, PaymentFilterCriteria criteria)
        {
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = (object?)criteria.CustomerId ?? DBNull.Value;
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = criteria.StartDate.HasValue
                ? criteria.StartDate.Value.ToDateTime(TimeOnly.MinValue)
                : DBNull.Value;
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = criteria.EndDate.HasValue
                ? criteria.EndDate.Value.ToDateTime(TimeOnly.MinValue)
                : DBNull.Value;
            command.Parameters.Add("@PaymentMode", SqlDbType.NVarChar, 20).Value = (object?)criteria.PaymentMode ?? DBNull.Value;
            command.Parameters.Add("@IsVoided", SqlDbType.Bit).Value = (object?)criteria.IsVoided ?? DBNull.Value;
            command.Parameters.Add("@Offset", SqlDbType.Int).Value = criteria.Offset;
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = criteria.Limit;
        }

        private static MonthlyBill MapMonthlyBillHeader(SqlDataReader reader)
        {
            return new MonthlyBill
            {
                BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                BillNumber = reader.GetString(reader.GetOrdinal("BillNumber")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                CustomerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? null : reader.GetString(reader.GetOrdinal("CustomerPhone")),
                RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                BillingMonth = reader.GetByte(reader.GetOrdinal("BillingMonth")),
                BillingYear = (short)reader.GetInt16(reader.GetOrdinal("BillingYear")),
                PeriodStartDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodStartDate"))),
                PeriodEndDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodEndDate"))),
                TotalLiquidMilkVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidMilkVolumeLitres")),
                CurrentMonthMilkAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthMilkAmount")),
                Adjustments = reader.GetDecimal(reader.GetOrdinal("Adjustments")),
                CurrentMonthInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthInvoiceAmount")),
                PreviousBalanceSnapshot = reader.GetDecimal(reader.GetOrdinal("PreviousBalanceSnapshot")),
                TotalStatementAmount = reader.GetDecimal(reader.GetOrdinal("TotalStatementAmount")),
                PaidAmount = reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                BalanceAmount = reader.GetDecimal(reader.GetOrdinal("BalanceAmount")),
                PaymentStatus = reader.GetString(reader.GetOrdinal("PaymentStatus")),
                IsPublished = reader.GetBoolean(reader.GetOrdinal("IsPublished")),
                PublishedAt = reader.IsDBNull(reader.GetOrdinal("PublishedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PublishedAt")),
                Version = reader.GetInt32(reader.GetOrdinal("Version")),
                RevisionReason = reader.IsDBNull(reader.GetOrdinal("RevisionReason")) ? null : reader.GetString(reader.GetOrdinal("RevisionReason")),
                PaymentDueDate = reader.IsDBNull(reader.GetOrdinal("PaymentDueDate"))
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDueDate"))),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
                UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
                UpdatedBy = reader.IsDBNull(reader.GetOrdinal("UpdatedBy")) ? null : reader.GetInt32(reader.GetOrdinal("UpdatedBy"))
            };
        }

        private static MonthlyBill MapMonthlyBillSummaryRow(SqlDataReader reader)
        {
            return new MonthlyBill
            {
                BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                BillNumber = reader.GetString(reader.GetOrdinal("BillNumber")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                CustomerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? null : reader.GetString(reader.GetOrdinal("CustomerPhone")),
                RouteName = reader.IsDBNull(reader.GetOrdinal("RouteName")) ? null : reader.GetString(reader.GetOrdinal("RouteName")),
                BillingMonth = reader.GetByte(reader.GetOrdinal("BillingMonth")),
                BillingYear = (short)reader.GetInt16(reader.GetOrdinal("BillingYear")),
                PeriodStartDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodStartDate"))),
                PeriodEndDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PeriodEndDate"))),
                TotalLiquidMilkVolumeLitres = reader.GetDecimal(reader.GetOrdinal("TotalLiquidMilkVolumeLitres")),
                CurrentMonthMilkAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthMilkAmount")),
                Adjustments = reader.GetDecimal(reader.GetOrdinal("Adjustments")),
                CurrentMonthInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("CurrentMonthInvoiceAmount")),
                PreviousBalanceSnapshot = reader.GetDecimal(reader.GetOrdinal("PreviousBalanceSnapshot")),
                TotalStatementAmount = reader.GetDecimal(reader.GetOrdinal("TotalStatementAmount")),
                PaidAmount = reader.GetDecimal(reader.GetOrdinal("PaidAmount")),
                BalanceAmount = reader.GetDecimal(reader.GetOrdinal("BalanceAmount")),
                PaymentStatus = reader.GetString(reader.GetOrdinal("PaymentStatus")),
                IsPublished = reader.GetBoolean(reader.GetOrdinal("IsPublished")),
                PublishedAt = reader.IsDBNull(reader.GetOrdinal("PublishedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("PublishedAt")),
                Version = reader.GetInt32(reader.GetOrdinal("Version")),
                RevisionReason = reader.IsDBNull(reader.GetOrdinal("RevisionReason")) ? null : reader.GetString(reader.GetOrdinal("RevisionReason")),
                PaymentDueDate = reader.IsDBNull(reader.GetOrdinal("PaymentDueDate"))
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDueDate"))),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy"))
            };
        }

        private static MonthlyBillItem MapMonthlyBillItem(SqlDataReader reader)
        {
            return new MonthlyBillItem
            {
                BillItemId = reader.GetInt64(reader.GetOrdinal("BillItemId")),
                BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                MilkCategoryId = reader.GetInt32(reader.GetOrdinal("MilkCategoryId")),
                CategoryName = reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
                DeliveredDaysCount = reader.GetInt32(reader.GetOrdinal("DeliveredDaysCount")),
                QuantityCount = (int)Math.Round(reader.GetDecimal(reader.GetOrdinal("QuantityCount"))),
                LiquidVolumeLitres = reader.GetDecimal(reader.GetOrdinal("LiquidVolumeLitres")),
                UnitPriceSnapshot = reader.GetDecimal(reader.GetOrdinal("UnitPriceSnapshot")),
                SubTotal = reader.GetDecimal(reader.GetOrdinal("SubTotal"))
            };
        }

        private static MonthlyBillRevision MapMonthlyBillRevision(SqlDataReader reader)
        {
            return new MonthlyBillRevision
            {
                RevisionId = reader.GetInt64(reader.GetOrdinal("RevisionId")),
                BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                VersionNumber = reader.GetInt32(reader.GetOrdinal("VersionNumber")),
                PriorMilkAmount = reader.GetDecimal(reader.GetOrdinal("PriorMilkAmount")),
                NewMilkAmount = reader.GetDecimal(reader.GetOrdinal("NewMilkAmount")),
                PriorAdjustments = reader.GetDecimal(reader.GetOrdinal("PriorAdjustments")),
                NewAdjustments = reader.GetDecimal(reader.GetOrdinal("NewAdjustments")),
                PriorInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("PriorInvoiceAmount")),
                NewInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("NewInvoiceAmount")),
                PreviousBalanceSnapshot = reader.GetDecimal(reader.GetOrdinal("PreviousBalanceSnapshot")),
                PaidAmountSnapshot = reader.GetDecimal(reader.GetOrdinal("PaidAmountSnapshot")),
                PriorBalanceAmount = reader.GetDecimal(reader.GetOrdinal("PriorBalanceAmount")),
                NewBalanceAmount = reader.GetDecimal(reader.GetOrdinal("NewBalanceAmount")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                RevisedBy = reader.GetInt32(reader.GetOrdinal("RevisedBy")),
                RevisedByUserName = reader.IsDBNull(reader.GetOrdinal("RevisedByUserName")) ? null : reader.GetString(reader.GetOrdinal("RevisedByUserName")),
                RevisedAt = reader.GetDateTime(reader.GetOrdinal("RevisedAt"))
            };
        }

        private static PaymentAllocationEvent MapPaymentAllocationEvent(SqlDataReader reader)
        {
            return new PaymentAllocationEvent
            {
                AllocationEventId = reader.GetInt64(reader.GetOrdinal("AllocationEventId")),
                PaymentId = reader.GetInt64(reader.GetOrdinal("PaymentId")),
                ReceiptNumber = reader.IsDBNull(reader.GetOrdinal("ReceiptNumber")) ? null : reader.GetString(reader.GetOrdinal("ReceiptNumber")),
                BillId = reader.GetInt64(reader.GetOrdinal("BillId")),
                BillNumber = reader.IsDBNull(reader.GetOrdinal("BillNumber")) ? null : reader.GetString(reader.GetOrdinal("BillNumber")),
                EventType = reader.GetString(reader.GetOrdinal("EventType")),
                OriginalAllocationEventId = reader.IsDBNull(reader.GetOrdinal("OriginalAllocationEventId")) ? null : reader.GetInt64(reader.GetOrdinal("OriginalAllocationEventId")),
                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                Direction = reader.GetInt16(reader.GetOrdinal("Direction")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                RelatedRevisionId = reader.IsDBNull(reader.GetOrdinal("RelatedRevisionId")) ? null : reader.GetInt64(reader.GetOrdinal("RelatedRevisionId")),
                LegacyAllocationId = reader.IsDBNull(reader.GetOrdinal("LegacyAllocationId")) ? null : reader.GetInt64(reader.GetOrdinal("LegacyAllocationId")),
                PerformedBy = reader.GetInt32(reader.GetOrdinal("PerformedBy")),
                PerformedByUserName = reader.IsDBNull(reader.GetOrdinal("PerformedByUserName")) ? string.Empty : reader.GetString(reader.GetOrdinal("PerformedByUserName")),
                PerformedAt = reader.GetDateTime(reader.GetOrdinal("PerformedAt"))
            };
        }

        private static Payment MapPaymentRecord(SqlDataReader reader)
        {
            return new Payment
            {
                PaymentId = reader.GetInt64(reader.GetOrdinal("PaymentId")),
                ReceiptNumber = reader.GetString(reader.GetOrdinal("ReceiptNumber")),
                CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
                CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                CustomerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? null : reader.GetString(reader.GetOrdinal("CustomerPhone")),
                PaymentDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("PaymentDate"))),
                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                PaymentMode = reader.GetString(reader.GetOrdinal("PaymentMode")),
                ReferenceNumber = reader.IsDBNull(reader.GetOrdinal("ReferenceNumber")) ? null : reader.GetString(reader.GetOrdinal("ReferenceNumber")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                IsVoided = reader.GetBoolean(reader.GetOrdinal("IsVoided")),
                VoidedAt = reader.IsDBNull(reader.GetOrdinal("VoidedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("VoidedAt")),
                VoidReason = reader.IsDBNull(reader.GetOrdinal("VoidReason")) ? null : reader.GetString(reader.GetOrdinal("VoidReason")),
                ReceivedBy = reader.GetInt32(reader.GetOrdinal("ReceivedBy")),
                ReceivedByUserName = reader.IsDBNull(reader.GetOrdinal("ReceivedByUserName")) ? null : reader.GetString(reader.GetOrdinal("ReceivedByUserName")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                AllocatedAmount = reader.GetDecimal(reader.GetOrdinal("AllocatedAmount"))
            };
        }

        #endregion
    }
}
