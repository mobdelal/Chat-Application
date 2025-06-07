using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class ToggleMuteStatusDTO
    {
        public int ChatId { get; set; }
        public int UserId { get; set; } 
        public bool IsMuted { get; set; } 
    }
}
