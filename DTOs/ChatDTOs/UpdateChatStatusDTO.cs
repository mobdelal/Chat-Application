using Models;
namespace DTOs.ChatDTOs
{
    public class UpdateChatStatusDTO
    {
        public int ChatId { get; set; }
        public ChatStatus NewStatus { get; set; }
        public int UserId { get; set; } 
    }
}
