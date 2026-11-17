using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Auth
{
    /// <summary>
    /// View model for field delivery boy login credentials.
    /// Delivery Boy authentication mandates Mobile Number (Phone) + Password.
    /// Does not impose arbitrary minimum password length on login to avoid blocking legitimate accounts.
    /// </summary>
    public class DeliveryBoyLoginViewModel
    {
        [Required(ErrorMessage = "Mobile number is required.")]
        [Phone(ErrorMessage = "Please enter a valid mobile number.")]
        [StringLength(15, MinimumLength = 10, ErrorMessage = "Mobile number must be between 10 and 15 digits.")]
        [RegularExpression(@"^[0-9+]+$", ErrorMessage = "Mobile number may only contain digits and an optional leading '+'.")]
        [Display(Name = "Mobile Number")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [StringLength(100, ErrorMessage = "Password cannot exceed 100 characters.")]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember Me")]
        public bool RememberMe { get; set; }
    }
}
