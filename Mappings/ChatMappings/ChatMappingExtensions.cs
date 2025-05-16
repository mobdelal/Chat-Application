using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using Models;
namespace Mappings.ChatMappings
{
    public static class ChatMappingExtensions
    {
        public static ChatDTO ToDTO(this Chat chat)
        {
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
                    Content = m.Content,
                    SentAt = m.SentAt,
                    EditedAt = m.EditedAt,
                    IsDeleted = m.IsDeleted,
                    Attachments = m.Attachments.Select(a => new FileAttachmentDTO
                    {
                        Id = a.Id,
                        FileUrl = a.FileUrl,
                        FileType = a.FileType
                    }).ToList(),
                    Reactions = m.Reactions.Select(r => new MessageReactionDTO
                    {
                        UserId = r.UserId,
                        Reaction = r.Reaction
                    }).ToList()
                }).ToList() ?? new()
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
                Content = message.Content,
                SentAt = message.SentAt,
                EditedAt = message.EditedAt,
                IsDeleted = message.IsDeleted,
                Attachments = message.Attachments.Select(a => new FileAttachmentDTO
                {
                    Id = a.Id,
                    FileUrl = a.FileUrl,
                    FileType = a.FileType
                }).ToList(),
                Reactions = message.Reactions.Select(r => new MessageReactionDTO
                {
                    UserId = r.UserId,
                    Reaction = r.Reaction
                }).ToList()
            };
        }
    }
}
  