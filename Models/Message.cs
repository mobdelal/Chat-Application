namespace Models
{
    public class Message
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public Chat Chat { get; set; } = null!;
        public int SenderId { get; set; }
        public User Sender { get; set; } = null!;
        public string? Content { get; set; } 
        public DateTime SentAt { get; set; }
        public DateTime? EditedAt { get; set; }

        public bool IsDeleted { get; set; }

        public ICollection<FileAttachment> Attachments { get; set; } = new List<FileAttachment>();
        public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
    }
}
