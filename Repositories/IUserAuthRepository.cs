using Dairy_Management_System.Models.Auth;

namespace Dairy_Management_System.Repositories
{
    /// <summary>
    /// Contract for database authentication data access against the dbo.Users table.
    /// Kevin will provide the concrete SQL Server ADO.NET implementation using Microsoft.Data.SqlClient.
    /// </summary>
    public interface IUserAuthRepository
    {
        /// <summary>
        /// Queries dbo.Users for an active staff member with Role = 'Admin', matching both Username and Phone.
        /// Expected SQL pattern:
        /// SELECT UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt
        /// FROM Users
        /// WHERE Username = @Username AND Phone = @Phone AND Role = 'Admin' AND IsActive = 1
        /// </summary>
        /// <param name="username">Unique username of the admin (case-insensitive handle).</param>
        /// <param name="phone">Unique registered mobile number of the admin.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A UserAuthRecord containing PasswordHash for verification, or null if no matching active admin exists.</returns>
        Task<UserAuthRecord?> GetActiveAdminByCredentialsAsync(string username, string phone, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries dbo.Users for an active staff member with Role = 'DeliveryBoy', matching Phone.
        /// Expected SQL pattern:
        /// SELECT UserId, Username, PasswordHash, Role, FullName, Phone, IsActive, LastLoginAt
        /// FROM Users
        /// WHERE Phone = @Phone AND Role = 'DeliveryBoy' AND IsActive = 1
        /// </summary>
        /// <param name="phone">Unique registered mobile number of the delivery boy.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A UserAuthRecord containing PasswordHash for verification, or null if no matching active delivery boy exists.</returns>
        Task<UserAuthRecord?> GetActiveDeliveryBoyByPhoneAsync(string phone, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the LastLoginAt timestamp upon successful authentication.
        /// Expected SQL pattern:
        /// UPDATE Users SET LastLoginAt = @LastLoginUtc, UpdatedAt = SYSUTCDATETIME() WHERE UserId = @UserId
        /// </summary>
        /// <param name="userId">Unique internal surrogate identifier (Users.UserId).</param>
        /// <param name="lastLoginUtc">Current UTC timestamp.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginUtc, CancellationToken cancellationToken = default);
    }
}
