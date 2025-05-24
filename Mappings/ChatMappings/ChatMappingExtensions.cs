using DTOs;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using Models;
using System.Linq;

namespace Mappings.ChatMappings
{
    public static class ChatMappingExtensions
    {
        public static ChatDTO ToDTO(this Chat chat, int currentUserId) 
        {
            var currentUserParticipant = chat.Participants
                .FirstOrDefault(p => p.UserId == currentUserId);

            var unreadCount = chat.Messages
                .Count(m => (currentUserParticipant?.LastReadAt == null || m.SentAt > currentUserParticipant.LastReadAt)
                            && m.SenderId != currentUserId);

            // Get the last message
            var lastMessage = chat.Messages
                .Where(m => !m.IsDeleted) 
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            return new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                AvatarUrl = chat.AvatarUrl,
                IsGroup = chat.IsGroup,
                CreatedAt = chat.CreatedAt,

                Participants = chat.Participants?.Select(p => new ChatParticipantDTO
                {
                    UserId = p.UserId,
                    Username = p.User?.Username ?? "",
                    AvatarUrl = p.User?.AvatarUrl,
                    IsAdmin = p.IsAdmin,
                    JoinedAt = p.JoinedAt
                }).ToList() ?? new(),

                Messages = chat.Messages?.Select(m => new MessageDTO 
                {
                    Id = m.Id,
                    ChatId = m.ChatId,
                    SenderId = m.SenderId,
                    SenderUsername = m.Sender?.Username ?? "",
                    SenderAvatarUrl = m.Sender?.AvatarUrl,
                    Content = m.Content,
                    SentAt = m.SentAt,
                    EditedAt = m.EditedAt,
                    IsDeleted = m.IsDeleted,
                    Attachments = m.Attachments?.Select(a => new FileAttachmentDTO
                    {
                        Id = a.Id,
                        FileUrl = a.FileUrl,
                        FileType = a.FileType
                    }).ToList() ?? new(),
                    Reactions = m.Reactions?.Select(r => new MessageReactionDTO
                    {
                        UserId = r.UserId,
                        Reaction = r.Reaction
                    }).ToList() ?? new()
                }).ToList() ?? new(),

                UnreadCount = unreadCount,
                LastMessage = lastMessage?.ToDTO() 
            };
        }

        public static MessageDTO ToDTO(this Message message)
        {
            return new MessageDTO
            {
                Id = message.Id,
                ChatId = message.ChatId,
                SenderId = message.SenderId,
                SenderUsername = message.Sender?.Username ?? string.Empty,
                SenderAvatarUrl = message.Sender?.AvatarUrl,
                Content = message.Content,
                SentAt = message.SentAt,
                EditedAt = message.EditedAt,
                IsDeleted = message.IsDeleted,
                Attachments = message.Attachments?.Select(a => new FileAttachmentDTO
                {
                    Id = a.Id,
                    FileUrl = a.FileUrl,
                    FileType = a.FileType
                }).ToList() ?? new(),
                Reactions = message.Reactions?.Select(r => new MessageReactionDTO
                {
                    UserId = r.UserId,
                    Reaction = r.Reaction
                }).ToList() ?? new()
            };
        }
        public static NewMessageNotificationDTO ToNewMessageNotificationDTO(
            this Message message,
            string chatName,
            string chatAvatarUrl,
            int unreadCountInChat,
            int totalUnreadCount)
        {
            if (message == null) return null;

            return new NewMessageNotificationDTO
            {
                ChatId = message.ChatId,
                ChatName = chatName,
                ChatAvatarUrl = chatAvatarUrl,
                SenderId = message.SenderId,
                SenderUsername = message.Sender?.Username,
                ContentSnippet = message.Content?.Length > 50 ? message.Content.Substring(0, 50) + "..." : message.Content,
                SentAt = message.SentAt,
                UnreadCountInChat = unreadCountInChat,
                TotalUnreadCount = totalUnreadCount
            };
        }
    }
}