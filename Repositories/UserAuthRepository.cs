using System.Data;
using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Models.Settings;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Production-grade SQL Server ADO.NET repository implementing IUserAuthRepository using Microsoft.Data.SqlClient.
    /// Manages parameterized queries, asynchronous streaming, resource disposal, and cancellation propagation.
    /// Strictly internalizes data access and never performs password verification or plaintext logging.
    /// </summary>
    public class UserAuthRepository : IUserAuthRepository, IUserAccountRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<UserAuthRepository> _logger;

        public UserAuthRepository(ISqlConnectionFactory connectionFactory, ILogger<UserAuthRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public UserAuthRepository(IConfiguration configuration, ILogger<UserAuthRepository> logger)
            : this(new SqlConnectionFactory(configuration), logger)
        {
        }

        /// <summary>
        /// Secondary constructor for scenarios where connection string is directly provided (e.g. testing).
        /// </summary>
        public UserAuthRepository(string connectionString, ILogger<UserAuthRepository> logger)
            : this(new SqlConnectionFactory(connectionString), logger)
        {
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

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

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

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

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

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

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

        public async Task<UserProfileDto?> GetProfileByIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Users_GetProfileById", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            int ordId = reader.GetOrdinal("UserId");
            int ordUsername = reader.GetOrdinal("Username");
            int ordRole = reader.GetOrdinal("Role");
            int ordFullName = reader.GetOrdinal("FullName");
            int ordPhone = reader.GetOrdinal("Phone");
            int ordIsActive = reader.GetOrdinal("IsActive");
            int ordLastLogin = reader.GetOrdinal("LastLoginAt");
            int ordCreated = reader.GetOrdinal("CreatedAt");

            return new UserProfileDto
            {
                UserId = reader.GetInt32(ordId),
                Username = reader.GetString(ordUsername),
                Role = reader.GetString(ordRole),
                FullName = reader.GetString(ordFullName),
                Phone = reader.GetString(ordPhone),
                IsActive = reader.GetBoolean(ordIsActive),
                LastLoginAt = reader.IsDBNull(ordLastLogin) ? null : reader.GetDateTime(ordLastLogin),
                CreatedAt = reader.GetDateTime(ordCreated)
            };
        }

        public async Task<UserAuthRecord?> GetAuthByIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Users_GetAuthById", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUserAuthRecord(reader);
            }
            return null;
        }

        public async Task<UserAuthRecord?> GetAuthByDeliveryBoyIdAsync(int deliveryBoyId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Users_GetAuthByDeliveryBoyId", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            command.Parameters.Add(new SqlParameter("@DeliveryBoyId", SqlDbType.Int) { Value = deliveryBoyId });

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUserAuthRecord(reader);
            }
            return null;
        }

        public async Task UpdateOwnProfileAsync(int userId, string? username, string fullName, string phone, string? vehicleNumber, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Users_UpdateOwnProfile", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };

            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
            command.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 50)
            {
                Value = string.IsNullOrWhiteSpace(username) ? DBNull.Value : username.Trim()
            });
            command.Parameters.Add(new SqlParameter("@FullName", SqlDbType.NVarChar, 100) { Value = fullName.Trim() });
            command.Parameters.Add(new SqlParameter("@Phone", SqlDbType.VarChar, 15) { Value = phone.Trim() });
            command.Parameters.Add(new SqlParameter("@VehicleNumber", SqlDbType.NVarChar, 30)
            {
                Value = string.IsNullOrWhiteSpace(vehicleNumber) ? DBNull.Value : vehicleNumber.Trim().ToUpperInvariant()
            });

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdatePasswordHashAsync(int userId, string newPasswordHash, int modifiedByUserId, CancellationToken cancellationToken = default)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_Users_UpdatePasswordHash", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };

            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
            command.Parameters.Add(new SqlParameter("@NewPasswordHash", SqlDbType.NVarChar, 256) { Value = newPasswordHash });
            command.Parameters.Add(new SqlParameter("@ModifiedByUserId", SqlDbType.Int) { Value = modifiedByUserId });

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
