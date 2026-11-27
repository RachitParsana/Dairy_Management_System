using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Models.Settings;

namespace Dairy_Management_System.Services
{
    /// <summary>
    /// Service contract for authenticating Admin and Delivery Boy credentials.
    /// Handles account existence, role validation, cryptographic password verification,
    /// and login timestamp auditing.
    /// </summary>
    public interface IUserAuthService
    {
        /// <summary>
        /// Authenticates an administrator via Username, Mobile Number, and Password.
        /// </summary>
        /// <param name="username">Admin username.</param>
        /// <param name="phone">Admin registered phone number.</param>
        /// <param name="password">Plaintext password submitted for verification.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>AuthResult containing AuthenticatedUser on success, or safe failure details.</returns>
        Task<AuthResult> AuthenticateAdminAsync(string username, string phone, string password, CancellationToken cancellationToken = default);

        /// <summary>
        /// Authenticates a delivery boy via Mobile Number and Password.
        /// </summary>
        /// <param name="phone">Delivery boy registered phone number.</param>
        /// <param name="password">Plaintext password submitted for verification.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>AuthResult containing AuthenticatedUser on success, or safe failure details.</returns>
        Task<AuthResult> AuthenticateDeliveryBoyAsync(string phone, string password, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Service contract for self-service user profile updates, password changes, and admin agent password resets.
    /// </summary>
    public interface IUserAccountService
    {
        Task<UserProfileDto?> GetProfileByIdAsync(int userId, CancellationToken cancellationToken = default);
        Task<UserProfileOperationResult> UpdateAdminProfileAsync(int userId, AdminProfileUpdateModel model, CancellationToken cancellationToken = default);
        Task<UserProfileOperationResult> UpdateDeliveryBoyProfileAsync(int userId, DeliveryBoyProfileUpdateModel model, CancellationToken cancellationToken = default);
        Task<UserProfileOperationResult> ChangeOwnPasswordAsync(int userId, ChangePasswordViewModel model, CancellationToken cancellationToken = default);
        Task<UserProfileOperationResult> ResetDeliveryBoyPasswordAsync(int deliveryBoyId, string newPassword, int adminUserId, CancellationToken cancellationToken = default);
    }
}
