using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class MessageReactionDTO
    {
        public int UserId { get; set; }
        public string Reaction { get; set; } = string.Empty;
    }

}
