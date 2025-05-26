using System.ComponentModel.DataAnnotations;

namespace DTOs.UserDTOs
{
    public class ChangePasswordDTO
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = null!;

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "New password must be between 6 and 100 characters.")]
        [DataType(DataType.Password)]
        [RegularExpression(
            @"^(?=.*\d)(?=.*[!@#$%^&*()_+=\[{\]};:<>|./?,-]).*$",
            ErrorMessage = "New password must contain at least one number and at least one special character."
        )]
        public string NewPassword { get; set; } = null!;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmNewPassword { get; set; } = null!;
    }
}