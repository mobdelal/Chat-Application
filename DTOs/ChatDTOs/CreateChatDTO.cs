using Microsoft.AspNetCore.Http;
namespace DTOs.ChatDTOs
{
    public class CreateChatDTO
    {
        public string Name { get; set; } = string.Empty;  
        public bool IsGroup { get; set; }
        public List<int> ParticipantIds { get; set; } = new();
        public int CreatedByUserId { get; set; }
        public IFormFile? AvatarUrl { get; set; }

    }
}
