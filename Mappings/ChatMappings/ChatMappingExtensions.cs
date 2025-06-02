using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using Models;
using System.Linq;
using System.Collections.Generic;
using DTOs; // Required for List<int>

namespace Mappings.ChatMappings
{
    public static class ChatMappingExtensions
    {
        public static ChatDTO ToDTO(this Chat chat, int currentUserId, List<int> blockedUserIds)
        {
            var currentUserParticipant = chat.Participants
                .FirstOrDefault(p => p.UserId == currentUserId);

            int lastReadMessageId = currentUserParticipant?.LastReadMessageId ?? 0;

            // --- ADIT: Conditional filtering for unread count based on chat type ---
            var unreadCount = chat.Messages.Count(m =>
                (currentUserParticipant?.LastReadAt == null || m.SentAt > currentUserParticipant.LastReadAt)
                && m.SenderId != currentUserId
                && !m.IsDeleted
                // Only exclude messages from blocked users if it's a group chat
                && !(chat.IsGroup && blockedUserIds.Contains(m.SenderId))
            );

            var lastMessage = chat.Messages
                .Where(m => !m.IsDeleted)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            var createdByUserId = chat.Participants.FirstOrDefault(p => p.IsAdmin)?.UserId ?? 0;

            var chatDto = new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                AvatarUrl = chat.AvatarUrl,
                IsGroup = chat.IsGroup,
                CreatedAt = chat.CreatedAt,
                Status = chat.Status,
                CreatedByUserId = createdByUserId,

                Participants = chat.Participants?.Select(p => new ChatParticipantDTO
                {
                    UserId = p.UserId,
                    Username = p.User?.Username ?? "",
                    AvatarUrl = p.User?.AvatarUrl,
                    IsAdmin = p.IsAdmin,
                    JoinedAt = p.JoinedAt
                }).ToList() ?? new(),

                // --- ADIT: Conditional filtering for messages collection based on chat type ---
                Messages = chat.Messages?
                    // Only filter out messages from blocked users if it's a group chat
                    .Where(m => !(chat.IsGroup && blockedUserIds.Contains(m.SenderId)))
                    .OrderBy(m => m.SentAt)
                    .Select(m => new MessageDTO
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
                        IsSystemMessage = m.IsSystemMessage,
                        IsReadByCurrentUser = (m.SenderId == currentUserId) || (m.Id <= lastReadMessageId),
                        Attachments = m.Attachments?.Select(a => new FileAttachmentDTO
                        {
                            Id = a.Id,
                            FileUrl = a.FileUrl,
                            FileType = a.FileType,
                            FileName = a.FileName
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

            // --- Start of specific edits for one-to-one chats ---
            // If it's a private chat (not a group)
            if (!chatDto.IsGroup)
            {
                var otherParticipant = chatDto.Participants.FirstOrDefault(p => p.UserId != currentUserId);

                chatDto.Name = otherParticipant?.Username ?? "Unknown User";
                chatDto.AvatarUrl = otherParticipant?.AvatarUrl;
            }
            else // It's a group chat
            {
                if (string.IsNullOrEmpty(chatDto.AvatarUrl))
                {
                    chatDto.AvatarUrl = "/images/default/groupImage.png"; // Example default group avatar
                }
            }
            // --- End of specific edits for one-to-one chats ---

            return chatDto;
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
                IsSystemMessage = message.IsSystemMessage,

                Attachments = message.Attachments?.Select(a => new FileAttachmentDTO
                {
                    Id = a.Id,
                    FileUrl = a.FileUrl,
                    FileType = a.FileType,
                    FileName = a.FileName
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

            if (message == null)
            {
                return new NewMessageNotificationDTO
                {
                };
            }

            return new NewMessageNotificationDTO
            {
                ChatId = message.ChatId,
                ChatName = chatName,
                ChatAvatarUrl = chatAvatarUrl,
                SenderId = message.SenderId,
                SenderUsername = message.Sender?.Username ?? string.Empty,
                ContentSnippet = message.Content?.Length > 50
                                     ? message.Content.Substring(0, 50) + "..."
                                     : message.Content ?? string.Empty,
                SentAt = message.SentAt,
                UnreadCountInChat = unreadCountInChat,
                TotalUnreadCount = totalUnreadCount
            };
        }
    }
}