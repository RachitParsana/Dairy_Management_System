using System;
using System.ComponentModel.DataAnnotations;
using Dairy_Management_System.Models.Auth;

namespace Dairy_Management_System.Models.Settings
{
    /// <summary>
    /// ViewModel for displaying and editing Administrator profile & password settings.
    /// </summary>
    public class AdminProfilePageViewModel
    {
        public int UserId { get; set; }
        public string Role { get; set; } = "Admin";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        public AdminProfileUpdateModel ProfileForm { get; set; } = new();
        public ChangePasswordViewModel PasswordForm { get; set; } = new();
    }

    public class AdminProfileUpdateModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mobile number is required.")]
        [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit Indian mobile number.")]
        [Display(Name = "Registered Mobile Number")]
        public string Phone { get; set; } = string.Empty;
    }

    public class DeliveryBoyProfileUpdateModel
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mobile number is required.")]
        [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit Indian mobile number.")]
        [Display(Name = "Registered Mobile Number")]
        public string Phone { get; set; } = string.Empty;

        [StringLength(30, ErrorMessage = "Vehicle number cannot exceed 30 characters.")]
        [Display(Name = "Vehicle Registration Number")]
        public string? VehicleNumber { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }

    public class UserProfileOperationResult
    {
        public bool Succeeded { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthenticatedUser? UpdatedUser { get; set; }

        public static UserProfileOperationResult Success(string message, AuthenticatedUser? updatedUser = null) => new()
        {
            Succeeded = true,
            Message = message,
            UpdatedUser = updatedUser
        };

        public static UserProfileOperationResult Failure(string errorMessage) => new()
        {
            Succeeded = false,
            ErrorMessage = errorMessage
        };
    }

    public class UserProfileDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
