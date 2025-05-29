using DTOs.ChatDTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.UserDTOs
{
    public class UserRelationshipStatusDTO
    {
        public List<UserDTO> BlockedUsers { get; set; } = new List<UserDTO>();
        public int TotalBlockedUsers { get; set; } 
        public List<ChatDTO> RejectedChats { get; set; } = new List<ChatDTO>();
        public int TotalRejectedChats { get; set; } 
    }
}
