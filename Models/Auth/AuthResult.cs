namespace Dairy_Management_System.Models.Auth
{
    /// <summary>
    /// Outcome of an authentication operation.
    /// Encapsulates success state, safe user-facing error messages, and authenticated identity.
    /// Prevents credential enumeration and timing attack disclosures.
    /// </summary>
    public class AuthResult
    {
        public bool Succeeded { get; init; }
        public string? ErrorMessage { get; init; }
        public AuthenticatedUser? User { get; init; }

        public static AuthResult Success(AuthenticatedUser user) => new()
        {
            Succeeded = true,
            User = user,
            ErrorMessage = null
        };

        public static AuthResult Failed(string message = "Invalid credentials. Please verify your login details.") => new()
        {
            Succeeded = false,
            ErrorMessage = message,
            User = null
        };
    }
}
