using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Data
{
    /// <summary>
    /// Production implementation of ISqlConnectionFactory.
    /// Provides consistent, pooled database connection management while safely enforcing the
    /// 'db_dms_app' least-privilege security boundary across all repository operations.
    /// </summary>
    public class SqlConnectionFactory : ISqlConnectionFactory
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlConnectionFactory>? _logger;

        public string ConnectionString => _connectionString;

        public SqlConnectionFactory(IConfiguration configuration, ILogger<SqlConnectionFactory>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _connectionString = configuration.GetConnectionString("DairyManagementDb")
                ?? throw new InvalidOperationException("Connection string 'DairyManagementDb' was not found in configuration.");
            _logger = logger;
        }

        public SqlConnectionFactory(string connectionString, ILogger<SqlConnectionFactory>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
            }
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task<AppDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // 1. Purge any dangling transition flags from connection pooling (defense in depth)
            // 2. If connected under an administrative principal (such as local development where LocalDB maps the desktop owner to dbo),
            //    immediately bind execution context to the restricted application user 'dms_app_user' (member of db_dms_app).
            //    In production with an unprivileged service account mapped directly to dms_app_user, this check safely no-ops.
            const string setupContextSql = @"
                EXEC sys.sp_set_session_context @key = N'SubscriptionScheduleTransition', @value = NULL;
                IF USER_NAME() != 'dms_app_user' AND (IS_MEMBER('db_owner') = 1 OR IS_SRVROLEMEMBER('sysadmin') = 1)
                BEGIN
                    EXECUTE AS USER = 'dms_app_user';
                END;";

            await using var cmd = new SqlCommand(setupContextSql, connection);
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return new AppDbConnection(connection);
        }

        public async Task<SqlConnection> CreateAdminConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
    }
}
