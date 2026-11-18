using System.Data;
using Dairy_Management_System.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Data
{
    /// <summary>
    /// Handles database initialization and secure initial Admin provisioning during application startup.
    /// Strictly verifies if an active Admin already exists before seeding to prevent duplicate accounts.
    /// Hashes credentials using ASP.NET Core PasswordHasher (PBKDF2) and never stores plaintext passwords.
    /// Does not seed fake Delivery Boy accounts; Delivery Boys are onboarded through official workflows.
    /// Handles database reachability exceptions gracefully so application startup is not crashed if the DB is temporarily offline.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            using var scope = serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAuthRecord>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

            var connectionString = configuration.GetConnectionString("DairyManagementDb");
            var connectionFactory = scope.ServiceProvider.GetService<ISqlConnectionFactory>()
                ?? (!string.IsNullOrWhiteSpace(connectionString) ? new SqlConnectionFactory(connectionString) : null);

            if (connectionFactory == null)
            {
                logger.LogWarning("Skipping initial Admin provisioning: 'DairyManagementDb' connection string is missing or empty.");
                return;
            }

            try
            {
                await SeedInitialAdminAsync(connectionFactory, configuration, passwordHasher, logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Initial Admin provisioning encountered a database exception. Application startup will continue without crashing.");
            }
        }

        private static async Task SeedInitialAdminAsync(
            ISqlConnectionFactory connectionFactory,
            IConfiguration configuration,
            IPasswordHasher<UserAuthRecord> passwordHasher,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            await using var connection = await connectionFactory.CreateAdminConnectionAsync(cancellationToken).ConfigureAwait(false);

            // Step 1: Check if an active Admin already exists in dbo.Users
            const string checkAdminSql = @"
                SELECT COUNT(1)
                FROM [dbo].[Users]
                WHERE [Role] = 'Admin' AND [IsActive] = 1;";

            await using (var checkCommand = new SqlCommand(checkAdminSql, connection))
            {
                var adminCount = (int)(await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
                if (adminCount > 0)
                {
                    logger.LogInformation("Initial Admin provisioning skipped: Active Admin account already exists (Count: {Count}).", adminCount);
                    return;
                }
            }

            // Step 2: Read initial Admin credentials from configuration (User Secrets or environment variables)
            var username = configuration["AdminSeed:Username"];
            var phone = configuration["AdminSeed:Phone"];
            var rawPassword = configuration["AdminSeed:Password"];
            var fullName = configuration["AdminSeed:FullName"] ?? "System Administrator";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(rawPassword))
            {
                logger.LogWarning(
                    "Initial Admin provisioning is not configured: required credentials (Username, Phone, Password) are missing. " +
                    "To provision the initial Admin account, configure 'AdminSeed:Username', 'AdminSeed:Phone', and 'AdminSeed:Password' " +
                    "via ASP.NET Core User Secrets (for local development) or environment variables (AdminSeed__Username, AdminSeed__Phone, AdminSeed__Password for deployment).");
                return;
            }

            // Step 3: Cryptographically hash initial password via PBKDF2
            var dummyRecord = new UserAuthRecord
            {
                Username = username,
                Phone = phone,
                Role = "Admin",
                FullName = fullName,
                IsActive = true
            };

            var passwordHash = passwordHasher.HashPassword(dummyRecord, rawPassword);

            // Step 4: Atomic insertion into dbo.Users with duplicate prevention
            const string insertAdminSql = @"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Username] = @Username OR [Phone] = @Phone)
                BEGIN
                    INSERT INTO [dbo].[Users] ([Username], [PasswordHash], [Role], [FullName], [Phone], [IsActive], [CreatedAt])
                    VALUES (@Username, @PasswordHash, 'Admin', @FullName, @Phone, 1, SYSUTCDATETIME());
                END;";

            await using (var insertCommand = new SqlCommand(insertAdminSql, connection))
            {
                insertCommand.CommandType = CommandType.Text;

                insertCommand.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 50) { Value = username.Trim() });
                insertCommand.Parameters.Add(new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 256) { Value = passwordHash });
                insertCommand.Parameters.Add(new SqlParameter("@FullName", SqlDbType.NVarChar, 100) { Value = fullName.Trim() });
                insertCommand.Parameters.Add(new SqlParameter("@Phone", SqlDbType.VarChar, 15) { Value = phone.Trim() });

                var rowsInserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (rowsInserted > 0)
                {
                    logger.LogInformation("Initial Admin account provisioned successfully (Username: {Username}, Phone: {Phone}).", username, phone);
                }
                else
                {
                    logger.LogInformation("Admin account with matching username or phone already exists. Skipping insertion.");
                }
            }
        }
    }
}
