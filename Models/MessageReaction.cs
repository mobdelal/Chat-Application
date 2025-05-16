namespace Models
{
    public class MessageReaction
    {
        public int Id { get; set; }

        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public string Reaction { get; set; } = null!;
    }
}
