namespace DTOs.UserDTOs
{
    public class UpdateUserDTO
    {
        public string? Username { get; set; } 
        public string? AvatarUrl { get; set; }
        public bool? ReceiveNotifications { get; set; } 
    }
}