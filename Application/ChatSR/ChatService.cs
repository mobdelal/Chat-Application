using Context;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using DTOs.Shared;
using Mappings.ChatMappings;
using Microsoft.EntityFrameworkCore;
using Models;
namespace Application.ChatSR
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        public ChatService(AppDbContext context)
        {
            _context = context;
        }


        public async Task<Result<ChatDTO>> CreateChatAsync(CreateChatDTO dto)
        {
            try
            {
                var now = DateTime.UtcNow;

                var participantIds = dto.ParticipantIds
                    .Distinct()
                    .Where(id => id != dto.CreatedByUserId)
                    .ToList();

                var users = participantIds.Any()
                    ? await _context.Users
                        .Where(u => participantIds.Contains(u.Id))
                        .ToListAsync()
                    : new List<User>();

                if (users.Count != participantIds.Count)
                    return Result<ChatDTO>.Failure("Some participants are invalid users.");

                string avatarUrl = string.Empty;

                if (dto.AvatarUrl != null)
                {
                    var allowedMimeTypes = new[]
                    {
                        "image/jpeg",
                        "image/png",
                        "image/gif",
                        "image/bmp",
                        "image/webp",
                        "image/tiff"
                    };
                    var mimeType = dto.AvatarUrl.ContentType.ToLower();

                    if (!allowedMimeTypes.Contains(mimeType))
                    {
                        return Result<ChatDTO>.Failure("Invalid file type. Please upload an image file (JPEG, PNG, GIF, BMP, WEBP, TIFF).");
                    }

                    var fileName = "chat_" + Guid.NewGuid() + Path.GetExtension(dto.AvatarUrl.FileName);
                    string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/chat_avatars");

                    Directory.CreateDirectory(uploadFolder); // Ensure directory exists

                    string filePath = Path.Combine(uploadFolder, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.AvatarUrl.CopyToAsync(fileStream);
                    }

                    avatarUrl = "/images/chat_avatars/" + fileName;
                }

                var chat = new Chat
                {
                    Name = dto.IsGroup ? dto.Name : string.Empty,
                    IsGroup = dto.IsGroup,
                    CreatedAt = now,
                    AvatarUrl = avatarUrl,
                    Participants = new List<ChatParticipant>()
                };

                // Add the creator as admin participant
                chat.Participants.Add(new ChatParticipant
                {
                    UserId = dto.CreatedByUserId,
                    IsAdmin = true,
                    IsMuted = false,
                    JoinedAt = now,
                    IsTyping = false
                });

                foreach (var userId in participantIds)
                {
                    chat.Participants.Add(new ChatParticipant
                    {
                        UserId = userId,
                        IsAdmin = false,
                        IsMuted = false,
                        JoinedAt = now,
                        IsTyping = false
                    });
                }

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();

                var createdChat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Reactions)
                    .FirstOrDefaultAsync(c => c.Id == chat.Id);

                if (createdChat == null)
                    return Result<ChatDTO>.Failure("Failed to load created chat.");

                return Result<ChatDTO>.Success(createdChat.ToDTO());
            }
            catch (Exception ex)
            {
                return Result<ChatDTO>.Failure($"An error occurred while creating the chat: {ex.Message}");
            }
        }
        public async Task<Result<List<MessageDTO>>> GetChatMessagesAsync(int chatId, int? lastMessageId = null, int pageSize = 50)
        {
            try
            {
                if (pageSize < 1 || pageSize > 100) pageSize = 50; 

                var query = _context.Messages
                    .Where(m => m.ChatId == chatId);

                if (lastMessageId.HasValue)
                {
                    query = query.Where(m => m.Id < lastMessageId.Value);
                }

                var messages = await query
                    .OrderByDescending(m => m.Id)
                    .Take(pageSize)
                    .Include(m => m.Attachments)
                    .Include(m => m.Reactions)
                    .ToListAsync();

                messages.Reverse();

                var messageDTOs = messages.Select(m => m.ToDTO()).ToList();

                return Result<List<MessageDTO>>.Success(messageDTOs);
            }
            catch (Exception ex)
            {
                return Result<List<MessageDTO>>.Failure($"Failed to load chat messages: {ex.Message}");
            }
        }
        public async Task<Result<List<ChatDTO>>> GetUserChatsAsync(int userId, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20; 

                var query = _context.Chats
                    .Where(c => c.Participants.Any(p => p.UserId == userId))
                    .OrderByDescending(c => c.CreatedAt);

                // Apply paging
                var chats = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Reactions)
                    .ToListAsync();

                var chatDTOs = chats.Select(c => c.ToDTO()).ToList();

                return Result<List<ChatDTO>>.Success(chatDTOs);
            }
            catch (Exception ex)
            {
                return Result<List<ChatDTO>>.Failure($"Error fetching chats: {ex.Message}");
            }
        }




        public async Task<Result<MessageDTO>> SendMessageAsync(SendMessageDTO dto)
        {
            try
            {
                var chat = await _context.Chats
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);
                if (chat == null)
                    return Result<MessageDTO>.Failure("Chat not found.");

                var sender = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == dto.SenderId);
                if (sender == null)
                    return Result<MessageDTO>.Failure("Sender not found.");

                var now = DateTime.UtcNow;

                var message = new Message
                {
                    ChatId = dto.ChatId,
                    SenderId = dto.SenderId,
                    Content = dto.Content,
                    SentAt = now,
                    IsDeleted = false
                };

                if (dto.Attachments != null && dto.Attachments.Any())
                {
                    var attachments = new List<FileAttachment>();
                    foreach (var fileDto in dto.Attachments)
                    {
                        if (fileDto.FileData != null && fileDto.FileData.Length > 0)
                        {
                            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(fileDto.FileData.FileName)}";

                            var savePath = Path.Combine("wwwroot", "uploads", fileName);

                            var directory = Path.GetDirectoryName(savePath);
                            if (!Directory.Exists(directory!))
                                Directory.CreateDirectory(directory!);

                            // Save the file to disk
                            using (var stream = new FileStream(savePath, FileMode.Create))
                            {
                                await fileDto.FileData.CopyToAsync(stream);
                            }

                            var fileUrl = $"/uploads/{fileName}";

                            attachments.Add(new FileAttachment
                            {
                                FileUrl = fileUrl,
                                FileType = fileDto.FileData.ContentType
                            });
                        }
                    }

                    message.Attachments = attachments;
                }

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                var createdMessage = await _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .Include(m => m.Reactions)
                    .FirstOrDefaultAsync(m => m.Id == message.Id);

                if (createdMessage == null)
                    return Result<MessageDTO>.Failure("Failed to load created message.");

                return Result<MessageDTO>.Success(createdMessage.ToDTO());
            }
            catch (Exception ex)
            {
                return Result<MessageDTO>.Failure($"An error occurred: {ex.Message}");
            }
        }

        public async Task<Result<bool>> AddParticipantAsync(int chatId, int userId)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                    return Result<bool>.Failure("Chat not found.");

                var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
                if (!userExists)
                    return Result<bool>.Failure("User not found.");

                var isAlreadyParticipant = chat.Participants.Any(p => p.UserId == userId);
                if (isAlreadyParticipant)
                    return Result<bool>.Failure("User is already a participant in this chat.");

                var participant = new ChatParticipant
                {
                    UserId = userId,
                    ChatId = chatId,
                    IsAdmin = false,
                    IsMuted = false,
                    JoinedAt = DateTime.UtcNow,
                    IsTyping = false
                };

                chat.Participants.Add(participant);

                await _context.SaveChangesAsync();

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while adding participant: {ex.Message}");
            }
        }
        public async Task<Result<bool>> RemoveParticipantAsync(int chatId, int userIdToRemove, int kickerUserId)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                    return Result<bool>.Failure("Chat not found.");

                var kickerParticipant = chat.Participants.FirstOrDefault(p => p.UserId == kickerUserId);
                if (kickerParticipant == null || !kickerParticipant.IsAdmin)
                    return Result<bool>.Failure("You do not have permission to remove participants.");

                var participantToRemove = chat.Participants.FirstOrDefault(p => p.UserId == userIdToRemove);
                if (participantToRemove == null)
                    return Result<bool>.Failure("User is not a participant in this chat.");

                if (participantToRemove.IsAdmin)
                    return Result<bool>.Failure("You cannot remove an admin participant.");

                chat.Participants.Remove(participantToRemove);

                await _context.SaveChangesAsync();

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while removing participant: {ex.Message}");
            }
        }
    }
}
