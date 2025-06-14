﻿using DTOs.MessageDTOs;
using Models;
namespace DTOs.ChatDTOs
{
    public class ChatDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsGroup { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }

        public List<ChatParticipantDTO> Participants { get; set; } = new();
        public List<MessageDTO> Messages { get; set; } = new();
        public int UnreadCount { get; set; } 
        public MessageDTO? LastMessage { get; set; }
        public ChatStatus Status { get; set; }
        public bool IsMutedForCurrentUser { get; set; }


    }
}
