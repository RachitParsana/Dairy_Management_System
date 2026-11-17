namespace Dairy_Management_System.Models.Auth
{
    /// <summary>
    /// Internal entity holding user data retrieved from dbo.Users table.
    /// Contains PasswordHash for cryptographic verification within IUserAuthService.
    /// Strictly internal: MUST NEVER be exposed outside the authentication service or returned in ViewModels/claims.
    /// </summary>
    public class UserAuthRecord
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
