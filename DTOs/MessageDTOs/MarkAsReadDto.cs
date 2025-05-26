using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class MarkAsReadDto
    {
        public int ChatId { get; set; }
        public int LastReadMessageId { get; set; }
    }
}
