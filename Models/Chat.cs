namespace Models
{
    public class Chat
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsGroup { get; set; }
        public DateTime CreatedAt { get; set; }
        public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }

}
