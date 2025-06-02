using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.ChatDTOs
{
    public class UpdateChatDTO
    {
        public int ChatId { get; set; }
        public int UserId { get; set; } 
        public string? Name { get; set; }
        public IFormFile? AvatarFile { get; set; } 
    }
}
