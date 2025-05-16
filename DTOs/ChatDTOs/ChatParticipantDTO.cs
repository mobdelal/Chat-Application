using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class ChatParticipantDTO
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime JoinedAt { get; set; }
    }

}
