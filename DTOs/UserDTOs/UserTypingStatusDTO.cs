﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTOs.UserDTOs
{
    public class UserTypingStatusDTO
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public string? Username { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
