using System.ComponentModel.DataAnnotations;
namespace DTOs.UserDTOs
{
    public class LoginDTO
    {
        [Required]
        public string UsernameOrEmail { get; set; } = null!;
        [Required]
        public string Password { get; set; } = null!;
    }
}
