namespace Models
{
    public class UserBlock
    {
        public int Id { get; set; }

        public int BlockerId { get; set; }

        public User Blocker { get; set; } = null!;

        public int BlockedId { get; set; }

        public User Blocked { get; set; } = null!;

        public DateTime BlockedAt { get; set; }
    }
}
