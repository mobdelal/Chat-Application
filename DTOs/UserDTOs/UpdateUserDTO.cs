using Microsoft.AspNetCore.Http;

namespace DTOs.UserDTOs
{
    public class UpdateUserDTO
    {
        public string? Username { get; set; }
        public IFormFile? AvatarFile { get; set; }
        public bool? ReceiveNotifications { get; set; } 
    }
}