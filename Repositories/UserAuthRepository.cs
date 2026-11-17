using System.Data;
using Dairy_Management_System.Models.Auth;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Production-grade SQL Server ADO.NET repository implementing IUserAuthRepository using Microsoft.Data.SqlClient.
    /// Manages parameterized queries, asynchronous streaming, resource disposal, and cancellation propagation.
    /// Strictly internalizes data access and never performs password verification or plaintext logging.
    /// </summary>
    public class UserAuthRepository : IUserAuthRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<UserAuthRepository> _logger;

        public UserAuthRepository(IConfiguration configuration, ILogger<UserAuthRepository> logger)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _connectionString = configuration.GetConnectionString("DairyManagementDb")
                ?? throw new InvalidOperationException("Connection string 'DairyManagementDb' was not found in configuration.");
        }

        /// <summary>
        /// Secondary constructor for scenarios where connection string is directly provided (e.g. testing).
        /// </summary>
        public UserAuthRepository(string connectionString, ILogger<UserAuthRepository> logger)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
            }

            _connectionString = connectionString;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserAuthRecord?> GetActiveAdminByCredentialsAsync(string username, string phone, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            const string sql = @"
                SELECT UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt
                FROM [dbo].[Users]
                WHERE [Username] = @Username
                  AND [Phone] = @Phone
                  AND [Role] = 'Admin'
                  AND [IsActive] = 1;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.CommandType = CommandType.Text;

            var usernameParam = command.Parameters.Add("@Username", SqlDbType.NVarChar, 50);
            usernameParam.Value = username.Trim();

            var phoneParam = command.Parameters.Add("@Phone", SqlDbType.VarChar, 15);
            phoneParam.Value = phone.Trim();

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUserAuthRecord(reader);
            }

            return null;
        }

        public async Task<UserAuthRecord?> GetActiveDeliveryBoyByPhoneAsync(string phone, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            const string sql = @"
                SELECT UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt
                FROM [dbo].[Users]
                WHERE [Phone] = @Phone
                  AND [Role] = 'DeliveryBoy'
                  AND [IsActive] = 1;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.CommandType = CommandType.Text;

            var phoneParam = command.Parameters.Add("@Phone", SqlDbType.VarChar, 15);
            phoneParam.Value = phone.Trim();

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUserAuthRecord(reader);
            }

            return null;
        }

        public async Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginUtc, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE [dbo].[Users]
                SET [LastLoginAt] = @LastLoginUtc,
                    [UpdatedAt] = SYSUTCDATETIME()
                WHERE [UserId] = @UserId;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(sql, connection);
            command.CommandType = CommandType.Text;

            var userIdParam = command.Parameters.Add("@UserId", SqlDbType.Int);
            userIdParam.Value = userId;

            var lastLoginParam = command.Parameters.Add("@LastLoginUtc", SqlDbType.DateTime2);
            lastLoginParam.Value = lastLoginUtc;

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rowsAffected == 0)
            {
                _logger.LogWarning("UpdateLastLoginAtAsync updated 0 rows for UserId {UserId}.", userId);
            }
        }

        private static UserAuthRecord MapUserAuthRecord(SqlDataReader reader)
        {
            var userIdOrdinal = reader.GetOrdinal("UserId");
            var usernameOrdinal = reader.GetOrdinal("Username");
            var passwordHashOrdinal = reader.GetOrdinal("PasswordHash");
            var roleOrdinal = reader.GetOrdinal("Role");
            var fullNameOrdinal = reader.GetOrdinal("FullName");
            var phoneOrdinal = reader.GetOrdinal("Phone");
            var isActiveOrdinal = reader.GetOrdinal("IsActive");
            var lastLoginAtOrdinal = reader.GetOrdinal("LastLoginAt");

            return new UserAuthRecord
            {
                UserId = reader.GetInt32(userIdOrdinal),
                Username = reader.GetString(usernameOrdinal),
                PasswordHash = reader.GetString(passwordHashOrdinal),
                Role = reader.GetString(roleOrdinal),
                FullName = reader.GetString(fullNameOrdinal),
                Phone = reader.GetString(phoneOrdinal),
                IsActive = reader.GetBoolean(isActiveOrdinal),
                LastLoginAt = reader.IsDBNull(lastLoginAtOrdinal) ? null : reader.GetDateTime(lastLoginAtOrdinal)
            };
        }
    }
}
