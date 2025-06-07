using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class LeaveGroupChatDTO
    {
        public int ChatId { get; set; }
        public int UserId { get; set; } // The ID of the user who is leaving the chat
    }
}
