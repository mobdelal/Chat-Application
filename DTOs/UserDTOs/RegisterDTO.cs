using Microsoft.AspNetCore.Http;

namespace DTOs.UserDTOs
{
    public class RegisterDTO
    {
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public IFormFile? Avatar { get; set; }

    }
}
