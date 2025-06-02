using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class AddParticipantRequest
    {
        [Required]
        public int UserIdToAdd { get; set; }
    }
}
