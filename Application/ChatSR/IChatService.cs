using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using DTOs.Shared;

namespace Application.ChatSR
{
    public interface IChatService
    {
        Task<Result<ChatDTO>> CreateChatAsync(CreateChatDTO dto);
        Task<Result<List<ChatDTO>>> GetUserChatsAsync(int userId, int pageNumber = 1, int pageSize = 20);
        Task<Result<bool>> AddParticipantAsync(int chatId, int userId);
        Task<Result<bool>> RemoveParticipantAsync(int chatId, int userIdToRemove, int kickerUserId);
        Task<Result<MessageDTO>> SendMessageAsync(SendMessageDTO dto);
        Task<Result<List<MessageDTO>>> GetChatMessagesAsync(int chatId, int? lastMessageId = null, int pageSize = 50);
    }
}
