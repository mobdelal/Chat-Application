using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.MessageDTOs
{
    public class EditMessageDTO
    {
        [Required]
        public int MessageId { get; set; }

        [Required]
        public int ChatId { get; set; } // To ensure message belongs to the correct chat for security

        [Required]
        public int UserId { get; set; } // The ID of the user requesting the edit (for authorization)

        [Required]
        [StringLength(500, MinimumLength = 1, ErrorMessage = "Message content must be between 1 and 500 characters.")]
        public string NewContent { get; set; } = string.Empty; // The updated message content
    }
}
