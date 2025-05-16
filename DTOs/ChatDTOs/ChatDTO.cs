using DTOs.MessageDTOs;
namespace DTOs.ChatDTOs
{
    public class ChatDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsGroup { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<ChatParticipantDTO> Participants { get; set; } = new();
        public List<MessageDTO> Messages { get; set; } = new();
    }
}
