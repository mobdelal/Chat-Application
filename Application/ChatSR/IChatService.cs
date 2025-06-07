using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using DTOs.Shared;

namespace Application.ChatSR
{
    public interface IChatService
    {
        Task<Result<ChatDTO>> CreateChatAsync(CreateChatDTO dto);
        Task<Result<List<ChatDTO>>> GetUserChatsAsync(int userId, int pageNumber = 1, int pageSize = 20);
        Task<Result<ChatDTO>> GetChatByIdAsync(int chatId, int currentUserId);
        Task<Result<ChatDTO>> AddParticipantAsync(int chatId, int userIdToAdd, int adderUserId);
        Task<Result<bool>> RemoveParticipantAsync(int chatId, int userIdToRemove, int kickerUserId);
        Task<Result<MessageDTO>> SendMessageAsync(SendMessageDTO dto);
        Task<Result<List<MessageDTO>>> GetChatMessagesAsync(int chatId, int? lastMessageId = null, int pageSize = 50);
        Task<Result<List<int>>> GetUserChatIdsAsync(int userId);
        Task<Result<bool>> MarkMessagesAsReadAsync(int chatId, int userId, int lastReadMessageId);
        Task<Result<List<ChatDTO>>> SearchChatsByNameAsync(string searchTerm, int userId, int pageNumber = 1, int pageSize = 20);
        Task<Result<bool>> UpdateChatStatusAsync(UpdateChatStatusDTO dto);
        Task<Result<ChatDTO>> UpdateChatAsync(UpdateChatDTO dto);
        Task<Result<bool>> ToggleMuteStatusAsync(ToggleMuteStatusDTO dto);
        Task<Result<bool>> DeleteChatAsync(DeleteChatDTO dto);
        Task<Result<bool>> LeaveGroupChatAsync(LeaveGroupChatDTO dto);
        Task<Result<bool>> DeleteMessageAsync(DeleteMessageDTO dto);
        Task<Result<MessageDTO>> EditMessageAsync(EditMessageDTO dto);
        Task<Result<MessageDTO>> AddReactionAsync(AddReactionDTO dto); 
        Task<Result<MessageDTO>> RemoveReactionAsync(RemoveReactionDTO dto);











    }
}
