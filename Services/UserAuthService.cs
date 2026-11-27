using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Models.Settings;
using Dairy_Management_System.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Implements secure backend authentication for Admin and Delivery Boy accounts.
    /// Utilizes ASP.NET Core built-in PasswordHasher for timing-attack-resilient RFC 2898 PBKDF2 verification.
    /// Safely handles database exceptions and enforces fail-closed login audit persistence.
    /// </summary>
    public class UserAuthService : IUserAuthService, IUserAccountService
    {
        private readonly IUserAuthRepository? _userAuthRepository;
        private readonly IUserAccountRepository? _userAccountRepository;
        private readonly IPasswordHasher<UserAuthRecord> _passwordHasher;
        private readonly ILogger<UserAuthService> _logger;

        public UserAuthService(
            IUserAuthRepository? userAuthRepository,
            IPasswordHasher<UserAuthRecord> passwordHasher,
            ILogger<UserAuthService> logger)
        {
            _userAuthRepository = userAuthRepository;
            _userAccountRepository = userAuthRepository as IUserAccountRepository;
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

        public async Task<UserProfileDto?> GetProfileByIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (_userAccountRepository == null || userId <= 0)
            {
                return null;
            }
            return await _userAccountRepository.GetProfileByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<UserProfileOperationResult> UpdateAdminProfileAsync(int userId, AdminProfileUpdateModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_userAccountRepository == null)
            {
                return UserProfileOperationResult.Failure("User account repository is unavailable.");
            }

            try
            {
                await _userAccountRepository.UpdateOwnProfileAsync(
                    userId,
                    model.Username,
                    model.FullName,
                    model.Phone,
                    null,
                    cancellationToken).ConfigureAwait(false);

                var updated = await _userAccountRepository.GetProfileByIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var authUser = updated == null
                    ? null
                    : new AuthenticatedUser
                    {
                        UserId = updated.UserId,
                        Username = updated.Username,
                        FullName = updated.FullName,
                        Phone = updated.Phone,
                        Role = updated.Role,
                        IsActive = updated.IsActive
                    };

                return UserProfileOperationResult.Success("Administrator profile updated successfully.", authUser);
            }
            catch (SqlException ex) when (ex.Number is >= 50070 and <= 50079 or 2627 or 2601)
            {
                _logger.LogWarning(ex, "Validation failure updating Admin profile for UserId {UserId}.", userId);
                string message = ex.Number is 2627 or 2601
                    ? "Username or mobile number is already in use by another account."
                    : ex.Message;
                return UserProfileOperationResult.Failure(message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error updating Admin profile for UserId {UserId}.", userId);
                return UserProfileOperationResult.Failure("An error occurred while updating your profile.");
            }
        }

        public async Task<UserProfileOperationResult> UpdateDeliveryBoyProfileAsync(int userId, DeliveryBoyProfileUpdateModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_userAccountRepository == null)
            {
                return UserProfileOperationResult.Failure("User account repository is unavailable.");
            }

            try
            {
                await _userAccountRepository.UpdateOwnProfileAsync(
                    userId,
                    null,
                    model.FullName,
                    model.Phone,
                    model.VehicleNumber,
                    cancellationToken).ConfigureAwait(false);

                var updated = await _userAccountRepository.GetProfileByIdAsync(userId, cancellationToken).ConfigureAwait(false);
                var authUser = updated == null
                    ? null
                    : new AuthenticatedUser
                    {
                        UserId = updated.UserId,
                        Username = updated.Username,
                        FullName = updated.FullName,
                        Phone = updated.Phone,
                        Role = updated.Role,
                        IsActive = updated.IsActive
                    };

                return UserProfileOperationResult.Success("Delivery partner profile updated successfully.", authUser);
            }
            catch (SqlException ex) when (ex.Number is >= 50070 and <= 50079 or 2627 or 2601)
            {
                _logger.LogWarning(ex, "Validation failure updating DeliveryBoy profile for UserId {UserId}.", userId);
                string message = ex.Number is 2627 or 2601
                    ? "This mobile number is already registered to another user."
                    : ex.Message;
                return UserProfileOperationResult.Failure(message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error updating DeliveryBoy profile for UserId {UserId}.", userId);
                return UserProfileOperationResult.Failure("An error occurred while updating your profile.");
            }
        }

        public async Task<UserProfileOperationResult> ChangeOwnPasswordAsync(int userId, ChangePasswordViewModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_userAccountRepository == null)
            {
                return UserProfileOperationResult.Failure("User account repository is unavailable.");
            }

            if (string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword) || model.NewPassword.Length < 8)
            {
                return UserProfileOperationResult.Failure("Current password and a new password of at least 8 characters are required.");
            }

            if (!string.Equals(model.NewPassword, model.ConfirmNewPassword, StringComparison.Ordinal))
            {
                return UserProfileOperationResult.Failure("New password and confirmation do not match.");
            }

            if (string.Equals(model.CurrentPassword, model.NewPassword, StringComparison.Ordinal))
            {
                return UserProfileOperationResult.Failure("New password must be different from your current password.");
            }

            try
            {
                var userRecord = await _userAccountRepository.GetAuthByIdAsync(userId, cancellationToken).ConfigureAwait(false);
                if (userRecord == null || !userRecord.IsActive)
                {
                    return UserProfileOperationResult.Failure("Active user account not found.");
                }

                var verifyResult = _passwordHasher.VerifyHashedPassword(userRecord, userRecord.PasswordHash, model.CurrentPassword);
                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    _logger.LogWarning("Password change denied for UserId {UserId}: Current password mismatch.", userId);
                    return UserProfileOperationResult.Failure("Current password is incorrect.");
                }

                string newHash = _passwordHasher.HashPassword(userRecord, model.NewPassword);
                await _userAccountRepository.UpdatePasswordHashAsync(userId, newHash, userId, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("User {UserId} ({Role}) successfully changed their password.", userId, userRecord.Role);
                return UserProfileOperationResult.Success("Password updated successfully. Please use your new password on your next sign-in.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error changing password for UserId {UserId}.", userId);
                return UserProfileOperationResult.Failure("An error occurred while changing your password.");
            }
        }

        public async Task<UserProfileOperationResult> ResetDeliveryBoyPasswordAsync(int deliveryBoyId, string newPassword, int adminUserId, CancellationToken cancellationToken = default)
        {
            if (_userAccountRepository == null)
            {
                return UserProfileOperationResult.Failure("User account repository is unavailable.");
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                return UserProfileOperationResult.Failure("New password must be at least 8 characters long.");
            }

            try
            {
                var userRecord = await _userAccountRepository.GetAuthByDeliveryBoyIdAsync(deliveryBoyId, cancellationToken).ConfigureAwait(false);
                if (userRecord == null)
                {
                    return UserProfileOperationResult.Failure("Delivery boy user account not found.");
                }
                if (!userRecord.IsActive)
                {
                    return UserProfileOperationResult.Failure("Cannot reset password for an inactive delivery boy. Reactivate the agent first.");
                }

                string newHash = _passwordHasher.HashPassword(userRecord, newPassword);
                await _userAccountRepository.UpdatePasswordHashAsync(userRecord.UserId, newHash, adminUserId, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Admin {AdminUserId} reset password for DeliveryBoy {DeliveryBoyId} (UserId {UserId}).", adminUserId, deliveryBoyId, userRecord.UserId);
                return UserProfileOperationResult.Success($"Password reset successfully for {userRecord.FullName}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error resetting password for DeliveryBoyId {DeliveryBoyId}.", deliveryBoyId);
                return UserProfileOperationResult.Failure("An error occurred while resetting the delivery boy's password.");
            }
        }
    }
}
