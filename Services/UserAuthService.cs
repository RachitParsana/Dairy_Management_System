using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Implements secure backend authentication for Admin and Delivery Boy accounts.
    /// Utilizes ASP.NET Core built-in PasswordHasher for timing-attack-resilient RFC 2898 PBKDF2 verification.
    /// Safely handles database exceptions and enforces fail-closed login audit persistence.
    /// </summary>
    public class UserAuthService : IUserAuthService
    {
        private readonly IUserAuthRepository? _userAuthRepository;
        private readonly IPasswordHasher<UserAuthRecord> _passwordHasher;
        private readonly ILogger<UserAuthService> _logger;

        public UserAuthService(
            IUserAuthRepository? userAuthRepository,
            IPasswordHasher<UserAuthRecord> passwordHasher,
            ILogger<UserAuthService> logger)
        {
            _userAuthRepository = userAuthRepository;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<AuthResult> AuthenticateAdminAsync(string username, string phone, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
            {
                return AuthResult.Failed("Username, mobile number, and password are required.");
            }

            if (_userAuthRepository == null)
            {
                _logger.LogWarning("Authentication attempted but IUserAuthRepository is not registered. Pending Kevin's SQL Server ADO.NET integration.");
                return AuthResult.Failed("Database authentication is pending integration (SQL Server ADO.NET repository not registered).");
            }

            UserAuthRecord? user;
            try
            {
                user = await _userAuthRepository.GetActiveAdminByCredentialsAsync(username.Trim(), phone.Trim(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log technical error with username context only. Password and PasswordHash are NEVER logged.
                _logger.LogError(ex, "Database exception encountered during Admin credential retrieval for Username: {Username}.", username.Trim());
                return AuthResult.Failed("An unexpected database error occurred during authentication. Please try again.");
            }

            if (user == null)
            {
                _logger.LogInformation("Admin authentication failed: No active admin found matching submitted credentials.");
                return AuthResult.Failed("Invalid username, mobile number, or password.");
            }

            // Verify Role & Active status
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Admin authentication failed: User {UserId} does not have Admin role.", user.UserId);
                return AuthResult.Failed("Account is not authorized for administrative access.");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Admin authentication failed: Account {UserId} is inactive.", user.UserId);
                return AuthResult.Failed("Account is deactivated. Please contact your system administrator.");
            }

            // Check for empty/missing stored password hash
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                _logger.LogWarning("Admin authentication failed: User {UserId} has null, empty, or whitespace stored password hash.", user.UserId);
                return AuthResult.Failed("Invalid username, mobile number, or password.");
            }

            // Cryptographic Password Verification via ASP.NET Core PasswordHasher
            // Robustly protects against malformed Base64, corrupted payloads, or format exceptions
            PasswordVerificationResult verificationResult;
            try
            {
                verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Cryptographic verification exception for User {UserId}: Stored password hash is not valid Base64 or has an invalid header format.", user.UserId);
                return AuthResult.Failed("Invalid username, mobile number, or password.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected cryptographic exception encountered during password verification for User {UserId}.", user.UserId);
                return AuthResult.Failed("Invalid username, mobile number, or password.");
            }

            // Explicitly handle all PasswordVerificationResult values; unexpected enum values must fail closed
            switch (verificationResult)
            {
                case PasswordVerificationResult.Success:
                    // Password successfully verified against current PBKDF2 parameters
                    break;

                case PasswordVerificationResult.SuccessRehashNeeded:
                    // Password is valid, but Identity indicates work factors or algorithms should be upgraded.
                    // Authenticates successfully, but logs diagnostic warning without claiming hash was upgraded.
                    _logger.LogWarning("Admin password verified for User {UserId}, but PasswordHasher indicates rehash is needed (SuccessRehashNeeded). Hash upgrade is deferred until repository support is implemented.", user.UserId);
                    break;

                case PasswordVerificationResult.Failed:
                    _logger.LogInformation("Admin authentication failed: Password verification failed for User {UserId}.", user.UserId);
                    return AuthResult.Failed("Invalid username, mobile number, or password.");

                default:
                    _logger.LogError("Admin authentication failed: Unexpected PasswordVerificationResult '{Result}' for User {UserId}.", verificationResult, user.UserId);
                    return AuthResult.Failed("Invalid username, mobile number, or password.");
            }

            // Update LastLoginAt ONLY after successful password verification
            // Enforce fail-closed audit policy: failure to record login audit must not proceed silently
            try
            {
                await _userAuthRepository.UpdateLastLoginAtAsync(user.UserId, DateTime.UtcNow, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to update LastLoginAt audit timestamp for User {UserId}. Aborting login session to prevent audit inconsistency.", user.UserId);
                return AuthResult.Failed("Unable to finalize authentication audit record. Please try again.");
            }

            _logger.LogInformation("Admin user {UserId} ({Username}) authenticated successfully.", user.UserId, user.Username);

            var authenticatedUser = new AuthenticatedUser
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = "Admin",
                IsActive = user.IsActive
            };

            return AuthResult.Success(authenticatedUser);
        }

        public async Task<AuthResult> AuthenticateDeliveryBoyAsync(string phone, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
            {
                return AuthResult.Failed("Mobile number and password are required.");
            }

            if (_userAuthRepository == null)
            {
                _logger.LogWarning("Authentication attempted but IUserAuthRepository is not registered. Pending Kevin's SQL Server ADO.NET integration.");
                return AuthResult.Failed("Database authentication is pending integration (SQL Server ADO.NET repository not registered).");
            }

            UserAuthRecord? user;
            try
            {
                user = await _userAuthRepository.GetActiveDeliveryBoyByPhoneAsync(phone.Trim(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log technical error with phone context only. Password and PasswordHash are NEVER logged.
                _logger.LogError(ex, "Database exception encountered during Delivery Boy credential retrieval for Phone: {Phone}.", phone.Trim());
                return AuthResult.Failed("An unexpected database error occurred during authentication. Please try again.");
            }

            if (user == null)
            {
                _logger.LogInformation("Delivery Boy authentication failed: No active delivery boy found matching submitted phone.");
                return AuthResult.Failed("Invalid mobile number or password.");
            }

            // Verify Role & Active status
            if (!string.Equals(user.Role, "DeliveryBoy", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Delivery Boy authentication failed: User {UserId} does not have DeliveryBoy role.", user.UserId);
                return AuthResult.Failed("Account is not authorized for delivery partner access.");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Delivery Boy authentication failed: Account {UserId} is inactive.", user.UserId);
                return AuthResult.Failed("Account is deactivated. Please contact your system administrator.");
            }

            // Check for empty/missing stored password hash
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                _logger.LogWarning("Delivery Boy authentication failed: User {UserId} has null, empty, or whitespace stored password hash.", user.UserId);
                return AuthResult.Failed("Invalid mobile number or password.");
            }

            // Cryptographic Password Verification via ASP.NET Core PasswordHasher
            // Robustly protects against malformed Base64, corrupted payloads, or format exceptions
            PasswordVerificationResult verificationResult;
            try
            {
                verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Cryptographic verification exception for User {UserId}: Stored password hash is not valid Base64 or has an invalid header format.", user.UserId);
                return AuthResult.Failed("Invalid mobile number or password.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected cryptographic exception encountered during password verification for User {UserId}.", user.UserId);
                return AuthResult.Failed("Invalid mobile number or password.");
            }

            // Explicitly handle all PasswordVerificationResult values; unexpected enum values must fail closed
            switch (verificationResult)
            {
                case PasswordVerificationResult.Success:
                    // Password successfully verified against current PBKDF2 parameters
                    break;

                case PasswordVerificationResult.SuccessRehashNeeded:
                    // Password is valid, but Identity indicates work factors or algorithms should be upgraded.
                    // Authenticates successfully, but logs diagnostic warning without claiming hash was upgraded.
                    _logger.LogWarning("Delivery boy password verified for User {UserId}, but PasswordHasher indicates rehash is needed (SuccessRehashNeeded). Hash upgrade is deferred until repository support is implemented.", user.UserId);
                    break;

                case PasswordVerificationResult.Failed:
                    _logger.LogInformation("Delivery Boy authentication failed: Password verification failed for User {UserId}.", user.UserId);
                    return AuthResult.Failed("Invalid mobile number or password.");

                default:
                    _logger.LogError("Delivery Boy authentication failed: Unexpected PasswordVerificationResult '{Result}' for User {UserId}.", verificationResult, user.UserId);
                    return AuthResult.Failed("Invalid mobile number or password.");
            }

            // Update LastLoginAt ONLY after successful password verification
            // Enforce fail-closed audit policy: failure to record login audit must not proceed silently
            try
            {
                await _userAuthRepository.UpdateLastLoginAtAsync(user.UserId, DateTime.UtcNow, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to update LastLoginAt audit timestamp for User {UserId}. Aborting login session to prevent audit inconsistency.", user.UserId);
                return AuthResult.Failed("Unable to finalize authentication audit record. Please try again.");
            }

            _logger.LogInformation("Delivery boy user {UserId} authenticated successfully.", user.UserId);

            var authenticatedUser = new AuthenticatedUser
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = "DeliveryBoy",
                IsActive = user.IsActive
            };

            return AuthResult.Success(authenticatedUser);
        }
    }
}
