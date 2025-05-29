namespace DTOs.UserDTOs
{
    public class UserDetailsDTO
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public List<int> BlockedUsersIds { get; set; } = new List<int>();
        public bool? ReceiveNotifications { get; set; }

    }

}
