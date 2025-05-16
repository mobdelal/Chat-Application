using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class MessageDTO
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public int SenderId { get; set; }
        public string SenderUsername { get; set; } = string.Empty;
        public string? Content { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; }
        public List<FileAttachmentDTO> Attachments { get; set; } = new();
        public List<MessageReactionDTO> Reactions { get; set; } = new();
    }
}
