using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs
{
    public class NewMessageNotificationDTO
    {
        public int ChatId { get; set; }
        public string ChatName { get; set; } = string.Empty;
        public string ChatAvatarUrl { get; set; } = string.Empty;
        public int SenderId { get; set; }
        public string SenderUsername { get; set; } = string.Empty;
        public string ContentSnippet { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public int UnreadCountInChat { get; set; } 
        public int TotalUnreadCount { get; set; }
    }
}
