using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class AddReactionDTO
    {
        public int MessageId { get; set; }
        public int ChatId { get; set; } 
        public int UserId { get; set; }
        public string Reaction { get; set; } = string.Empty; 
    }
}
