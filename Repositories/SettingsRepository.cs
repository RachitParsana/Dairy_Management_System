using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Dairy_Management_System.Repositories
{
    public class SettingsRepository : ISettingsRepository
    {
        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<SettingsRepository> _logger;

        public SettingsRepository(ISqlConnectionFactory connectionFactory, ILogger<SettingsRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_AppSettings_GetAll", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var key = reader.GetString(reader.GetOrdinal("SettingKey"));
                    var val = reader.GetString(reader.GetOrdinal("SettingValue"));
                    dict[key] = val;
                }
                return dict;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve application settings from database.");
                throw;
            }
        }

        public async Task<bool> UpdateAsync(string key, string value, int adminUserId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Setting key cannot be empty.", nameof(key));

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand("dbo.usp_AppSettings_Update", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add(new SqlParameter("@SettingKey", SqlDbType.VarChar, 50) { Value = key.Trim() });
            command.Parameters.Add(new SqlParameter("@SettingValue", SqlDbType.NVarChar, 500) { Value = value?.Trim() ?? string.Empty });
            command.Parameters.Add(new SqlParameter("@AdminUserId", SqlDbType.Int) { Value = adminUserId });

            try
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update setting '{Key}' by Admin {AdminUserId}.", key, adminUserId);
                throw;
            }
        }
    }
}
