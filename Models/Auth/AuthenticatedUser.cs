namespace Dairy_Management_System.Models.Auth
{
    /// <summary>
    /// Represents an authenticated user session principal.
    /// Strictly security-safe: MUST NEVER contain PasswordHash or cryptographic credentials.
    /// </summary>
    public class AuthenticatedUser
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
