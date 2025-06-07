using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class DeleteChatDTO
    {
        public int ChatId { get; set; }
        public int RequestingUserId { get; set; } 
    }
}
