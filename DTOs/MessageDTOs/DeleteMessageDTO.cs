using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class DeleteMessageDTO
    {
        [Required]
        public int MessageId { get; set; }

        [Required]
        public int ChatId { get; set; } 

        [Required]
        public int UserId { get; set; } 
    }
}
