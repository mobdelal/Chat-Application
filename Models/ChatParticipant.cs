namespace Models
{
    public class ChatParticipant
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int ChatId { get; set; }
        public Chat? Chat { get; set; }
        public bool IsAdmin { get; set; }   
        public bool IsMuted { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool IsTyping { get; set; }
        public DateTime? LastReadAt { get; set; }
        public int? LastReadMessageId { get; set; }
        public Message? LastReadMessage { get; set; }
    }

}
