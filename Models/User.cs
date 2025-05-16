namespace Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();
        public ICollection<UserBlock> BlockedUsers { get; set; } = new List<UserBlock>();
        public ICollection<UserBlock> BlockedByUsers { get; set; } = new List<UserBlock>();


    }

}
