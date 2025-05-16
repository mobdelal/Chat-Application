namespace DTOs.UserDTOs
{
    public class UserDTO
    {
        public int Id { get; set; }

        public string Username { get; set; } = null!;  
        public string? AvatarUrl { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
