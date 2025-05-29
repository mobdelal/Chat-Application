using Application.FilesSR;
using Application.Hubs;
using Context;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using DTOs.Shared;
using Mappings.ChatMappings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Models;


namespace Application.ChatSR
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IConnectionMappingService _connectionMappingService;
        private readonly IFileStorageService _fileStorageService;

        public ChatService(AppDbContext context, IHubContext<ChatHub> hubContext, IConnectionMappingService connectionMappingService, IFileStorageService fileStorageService)
        {
            _context = context;
            _hubContext = hubContext;
            _connectionMappingService = connectionMappingService;
            _fileStorageService = fileStorageService;
        }

        public async Task<Result<ChatDTO>> CreateChatAsync(CreateChatDTO dto)
        {
            try
            {
                var now = DateTime.UtcNow;

                var allParticipantIds = dto.ParticipantIds.Distinct().ToList();
                if (!allParticipantIds.Contains(dto.CreatedByUserId))
                {
                    allParticipantIds.Add(dto.CreatedByUserId);
                }

                var users = await _context.Users
                    .Where(u => allParticipantIds.Contains(u.Id))
                    .ToListAsync();

                if (users.Count != allParticipantIds.Count)
                    return Result<ChatDTO>.Failure("Some participants are invalid users.");

                var creator = users.FirstOrDefault(u => u.Id == dto.CreatedByUserId);
                if (creator == null)
                    return Result<ChatDTO>.Failure("Creator user not found among provided participants.");

                if (!dto.IsGroup)
                {

                    var otherUserId = allParticipantIds.First(id => id != dto.CreatedByUserId);

                    var existingChat = await _context.Chats
                        .Include(c => c.Participants)
                        .Where(c => !c.IsGroup &&
                                    c.Participants.Any(p => p.UserId == dto.CreatedByUserId) &&
                                    c.Participants.Any(p => p.UserId == otherUserId))
                        .FirstOrDefaultAsync();

                    if (existingChat != null)
                    {
                        if (existingChat.Status == ChatStatus.Active)
                        {
                            return Result<ChatDTO>.Success(existingChat.ToDTO(dto.CreatedByUserId));
                        }
                        else if (existingChat.Status == ChatStatus.Pending)
                        {
                            return Result<ChatDTO>.Failure("A chat invitation is already pending with this user.");
                        }
                        else if (existingChat.Status == ChatStatus.Rejected)
                        {
                            return Result<ChatDTO>.Failure("This chat was previously rejected by the other user. You cannot send messages.");
                        }
                    }
                }
                string avatarUrl = string.Empty;

                if (dto.AvatarUrl != null)
                {
                    var allowedMimeTypes = new[]
                    {
                        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "image/tiff"
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
                else if (dto.IsGroup)
                {
                    avatarUrl = "/images/default/groupImage.png";
                }

                var chat = new Chat
                {
                    Name = dto.IsGroup ? dto.Name : "", 
                    IsGroup = dto.IsGroup,
                    CreatedAt = now,
                    AvatarUrl = avatarUrl,
                    Status = dto.IsGroup ? ChatStatus.Active : ChatStatus.Pending,
                    Participants = new List<ChatParticipant>()
                };

                foreach (var userId in allParticipantIds)
                {
                    chat.Participants.Add(new ChatParticipant
                    {
                        ChatId = chat.Id,
                        UserId = userId,
                        IsAdmin = (userId == dto.CreatedByUserId),
                        IsMuted = false,
                        JoinedAt = now,
                        IsTyping = false
                    });
                }

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();

                Message? systemMessage = null;

                if (chat.IsGroup)
                {
                    string systemMessageContent = $"{creator.Username} created this group.";

                    systemMessage = new Message
                    {
                        ChatId = chat.Id,
                        SenderId = creator.Id,
                        Content = systemMessageContent,
                        SentAt = now,
                        IsSystemMessage = true
                    };
                    _context.Messages.Add(systemMessage);
                    await _context.SaveChangesAsync();
                }

                var createdChatQuery = _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .AsNoTracking()
                    .Where(c => c.Id == chat.Id);

                if (systemMessage != null)
                {
                    createdChatQuery = createdChatQuery.Include(c => c.Messages.Where(m => m.Id == systemMessage.Id));
                }
                else
                {
                    createdChatQuery = createdChatQuery.Include(c => c.Messages);
                }

                var createdChat = await createdChatQuery.FirstOrDefaultAsync();

                if (createdChat == null)
                    return Result<ChatDTO>.Failure("Failed to load created chat after saving.");

                foreach (var participant in createdChat.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        await _hubContext.Groups.AddToGroupAsync(connectionId, $"Chat-{createdChat.Id}");

                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatCreated", createdChat.ToDTO(participant.UserId));

                        if (systemMessage != null)
                        {
                            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", systemMessage.ToDTO());
                        }
                    }
                }

                return Result<ChatDTO>.Success(createdChat.ToDTO(dto.CreatedByUserId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateChatAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<ChatDTO>.Failure($"An error occurred while creating the chat: {ex.Message}");
            }
        }
        public async Task<Result<List<MessageDTO>>> GetChatMessagesAsync(int chatId, int? lastMessageId = null, int pageSize = 50)
        {
            try
            {
                if (pageSize < 1 || pageSize > 100) pageSize = 50;

                var query = _context.Messages
                    .Where(m => m.ChatId == chatId && !m.IsDeleted);

                if (lastMessageId.HasValue)
                {
                    var lastMessageSentAt = await _context.Messages
                        .Where(m => m.Id == lastMessageId.Value)
                        .Select(m => m.SentAt)
                        .FirstOrDefaultAsync();

                    if (lastMessageSentAt != default)
                    {

                        query = query.Where(m => m.SentAt < lastMessageSentAt || (m.SentAt == lastMessageSentAt && m.Id < lastMessageId.Value));
                    }
                    else
                    {
                        return Result<List<MessageDTO>>.Success(new List<MessageDTO>());
                    }
                }

                var messages = await query
                    .OrderByDescending(m => m.SentAt)
                    .Take(pageSize)
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .Include(m => m.Reactions)
                    .ToListAsync();
                messages.Reverse();

                var messageDTOs = messages.Select(m => m.ToDTO()).ToList();

                return Result<List<MessageDTO>>.Success(messageDTOs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetChatMessagesAsync: {ex}");
                return Result<List<MessageDTO>>.Failure($"Failed to load chat messages: {ex.Message}");
            }
        }
        public async Task<Result<ChatDTO>> GetChatByIdAsync(int chatId, int currentUserId)
        {
            try
            {
                const int initialMessagePageSize = 50;

                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages
                        .Where(m => !m.IsDeleted)
                        .OrderByDescending(m => m.SentAt)
                        .Take(initialMessagePageSize))
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Reactions)
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                {
                    return Result<ChatDTO>.Failure("Chat not found.");
                }

                var currentUserParticipant = chat.Participants.FirstOrDefault(p => p.UserId == currentUserId);

                if (currentUserParticipant == null)
                {
                    return Result<ChatDTO>.Failure("You are not a participant of this chat.");
                }

                if (currentUserParticipant.LastReadAt == null || currentUserParticipant.LastReadAt < DateTime.UtcNow)
                {
                    currentUserParticipant.LastReadAt = DateTime.UtcNow;
                    _context.ChatParticipants.Update(currentUserParticipant);
                    await _context.SaveChangesAsync();
                }

                var chatDTO = chat.ToDTO(currentUserId);

                if (chatDTO.Messages != null && chatDTO.Messages.Any())
                {
                    chatDTO.Messages = chatDTO.Messages.OrderBy(m => m.SentAt).ToList();
                }


                return Result<ChatDTO>.Success(chatDTO);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetChatByIdAsync: {ex}");
                return Result<ChatDTO>.Failure($"An error occurred while fetching the chat: {ex.Message}");
            }
        }
        public async Task<Result<List<ChatDTO>>> GetUserChatsAsync(int userId, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                var query = _context.Chats
                    .AsNoTracking()
                    .Where(c => c.Participants.Any(p => p.UserId == userId));

                var chatsWithLatestActivity = await query
                    .Select(chat => new
                    {
                        Chat = chat,
                        LatestActivityDate = chat.Messages.Any(m => !m.IsDeleted)
                                             ? chat.Messages.Where(m => !m.IsDeleted).Max(m => m.SentAt)
                                             : chat.CreatedAt
                    })
                    .OrderByDescending(x => x.LatestActivityDate)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var chatIds = chatsWithLatestActivity.Select(x => x.Chat.Id).ToList();

                if (!chatIds.Any())
                {
                    return Result<List<ChatDTO>>.Success(new List<ChatDTO>());
                }

                var chats = await _context.Chats
                    .AsNoTracking()
                    .Where(c => chatIds.Contains(c.Id))
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Attachments)
                    // If you need reactions for lastMessage preview, include them
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Reactions)
                    .ToListAsync();

                var orderedChats = chatsWithLatestActivity
                    .Join(chats,
                          activity => activity.Chat.Id,
                          fullChat => fullChat.Id,
                          (activity, fullChat) => new { fullChat, activity.LatestActivityDate })
                    .OrderByDescending(x => x.LatestActivityDate)
                    .Select(x => x.fullChat)
                    .ToList();

                var chatDTOs = new List<ChatDTO>();

                foreach (var chat in orderedChats) // Iterate through the ordered chats
                {
                    var currentUserParticipant = chat.Participants
                        .FirstOrDefault(p => p.UserId == userId);

                    // Calculate UnreadCount
                    var unreadCount = chat.Messages
                        .Count(m => (currentUserParticipant?.LastReadAt == null || m.SentAt > currentUserParticipant.LastReadAt)
                                     && m.SenderId != userId && !m.IsDeleted); // Added !m.IsDeleted check

                    // Get LastMessage
                    var lastMessage = chat.Messages
                        .Where(m => !m.IsDeleted) // Ensure it's not a deleted message
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefault();

                    var createdByUserId = chat.Participants.FirstOrDefault(p => p.IsAdmin)?.UserId ?? 0;



                    var dto = new ChatDTO
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
                        Messages = new List<MessageDTO>(),
                        UnreadCount = unreadCount,
                        LastMessage = lastMessage?.ToDTO()
                    };

                    if (!dto.IsGroup)
                    {
                        var otherParticipant = dto.Participants.FirstOrDefault(p => p.UserId != userId);

                        dto.Name = otherParticipant?.Username ?? "Unknown User";
                        dto.AvatarUrl = otherParticipant?.AvatarUrl;

                        if (string.IsNullOrEmpty(dto.AvatarUrl))
                        {
                            dto.AvatarUrl = "/images/default/defaultUser.png";
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(dto.AvatarUrl))
                        {
                            dto.AvatarUrl = "/images/default/groupImage.png";
                        }
                    }

                    chatDTOs.Add(dto);
                }

                return Result<List<ChatDTO>>.Success(chatDTOs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetUserChatsAsync: {ex}");
                return Result<List<ChatDTO>>.Failure($"An error occurred while retrieving chats: {ex.Message}");
            }
        }
        public async Task<Result<MessageDTO>> SendMessageAsync(SendMessageDTO dto)
        {
            try
            {
                // 1. Validate sender and chat exist
                // No change needed here, FindAsync returns null if not found.
                var sender = await _context.Users.FindAsync(dto.SenderId);
                if (sender == null)
                {
                    return Result<MessageDTO>.Failure("Sender not found.");
                }

                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User) // Include User for participant to access Username/AvatarUrl later
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                if (chat == null)
                {
                    return Result<MessageDTO>.Failure("Chat not found.");
                }

                // Ensure chat.Participants is not null before using FirstOrDefault, though Include usually guarantees it.
                // It's good practice to ensure `chat.Participants` isn't null before calling methods on it,
                // even though `Include` should populate it.
                var senderParticipant = chat.Participants?.FirstOrDefault(p => p.UserId == dto.SenderId);
                if (senderParticipant == null)
                {
                    return Result<MessageDTO>.Failure("Sender is not a participant of this chat.");
                }

                var newMessage = new Message
                {
                    ChatId = dto.ChatId,
                    SenderId = dto.SenderId,
                    Content = dto.Content,
                    SentAt = DateTime.UtcNow,
                    Attachments = new List<FileAttachment>() // Initialize attachments list
                };

                // 2. Handle Attachments
                if (dto.Attachments != null && dto.Attachments.Any())
                {
                    foreach (var attachmentDto in dto.Attachments)
                    {
                        // Check if FileData is null or empty before processing
                        if (string.IsNullOrEmpty(attachmentDto.FileData))
                        {
                            return Result<MessageDTO>.Failure("Attachment file data (Base64 string) is missing.");
                        }

                        var base64Data = attachmentDto.FileData;
                        var commaIndex = base64Data.IndexOf(',');
                        if (commaIndex > -1)
                        {
                            base64Data = base64Data.Substring(commaIndex + 1);
                        }

                        byte[] fileBytes;
                        try
                        {
                            fileBytes = Convert.FromBase64String(base64Data);
                        }
                        catch (FormatException)
                        {
                            // Use null-conditional operator and null-coalescing for safer string operations
                            string fileNameForLog = attachmentDto.FileName ?? "UnknownFile";
                            string dataSnippet = base64Data.Length > 100 ? base64Data.Substring(0, 100) : base64Data;
                            Console.WriteLine($"Error decoding Base64 string for file '{fileNameForLog}'. Input: {dataSnippet}...");
                            return Result<MessageDTO>.Failure($"Invalid Base64 string format for attachment: {fileNameForLog}");
                        }

                        // SaveFileAsync should handle null or empty filename/filetype internally if necessary,
                        // but it's good to ensure you're not passing null if it expects non-null.
                        // Assuming FileName and FileType are non-nullable strings in FileAttachmentDTO.
                        var fileUrl = await _fileStorageService.SaveFileAsync(
                            fileBytes,
                            attachmentDto.FileName, // Assuming FileName is not null here, or add a null check if it can be.
                            attachmentDto.FileType // Assuming FileType is not null here.
                        );

                        if (string.IsNullOrEmpty(fileUrl))
                        {
                            return Result<MessageDTO>.Failure("Failed to upload attachment file.");
                        }

                        // Ensure FileName and FileType are non-null when creating FileAttachment model
                        newMessage.Attachments.Add(new FileAttachment
                        {
                            FileName = attachmentDto.FileName ?? "unnamed_file", // Provide a default if FileName can be null
                            FileUrl = fileUrl,
                            FileType = attachmentDto.FileType ?? "application/octet-stream", // Provide a default if FileType can be null
                        });
                    }
                }

                _context.Messages.Add(newMessage);
                await _context.SaveChangesAsync();

                // 3. Update LastReadMessageId for the sender
                // `senderParticipant` is already checked for null above.
                senderParticipant.LastReadMessageId = newMessage.Id;
                await _context.SaveChangesAsync();

                // 4. Retrieve the created message with its sender and attachments
                var createdMessage = await _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .FirstOrDefaultAsync(m => m.Id == newMessage.Id);

                // This check is still necessary, even if it "shouldn't" happen.
                if (createdMessage == null)
                {
                    return Result<MessageDTO>.Failure("Failed to retrieve the newly created message after saving.");
                }

                // If `createdMessage` is not null here, its `Sender` and `Attachments` properties
                // might still be null if they weren't found in the database.
                // However, ToDTO() mapping methods should handle nulls gracefully using null-conditional operators.
                var messageDTO = createdMessage.ToDTO();

                // 5. Notify other participants in the chat about the new message
                // Ensure chat.Participants is not null. Use null-conditional operator for safety.
                foreach (var participant in chat.Participants ?? Enumerable.Empty<ChatParticipant>())
                {
                    if (participant.UserId == dto.SenderId) continue;

                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    if (connections.Any())
                    {
                        var unreadCountInChat = await GetUnreadMessageCountForChatAsync(chat.Id, participant.UserId);
                        var totalUnreadCount = await GetTotalUnreadMessageCountForUserAsync(participant.UserId);

                        // Use null-conditional operator for createdMessage.Sender.Username for safety.
                        // Also, ensure chat.Name is not null (though it should be if chat is valid).
                        var notification = createdMessage.ToNewMessageNotificationDTO(
                            chat.IsGroup ? chat.Name ?? "Group Chat" : createdMessage.Sender?.Username ?? "Unknown User",
                            chat.AvatarUrl ?? "", // Provide a default empty string if AvatarUrl can be null
                            unreadCountInChat,
                            totalUnreadCount
                        );

                        foreach (var connectionId in connections)
                        {
                            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveNewMessage", messageDTO);
                            await _hubContext.Clients.Client(connectionId).SendAsync("NewMessageNotification", notification);
                        }
                    }
                }

                // 6. Notify the sender's own clients
                var senderConnections = _connectionMappingService.GetConnections(dto.SenderId);
                if (senderConnections.Any())
                {
                    var unreadCountInChatForSender = await GetUnreadMessageCountForChatAsync(chat.Id, dto.SenderId);
                    var totalUnreadCountForSender = await GetTotalUnreadMessageCountForUserAsync(dto.SenderId);

                    // For private chats, find the other participant safely.
                    // Use null-conditional and null-coalescing for robustness.
                    var otherParticipantUsername = chat.Participants
                                                    ?.FirstOrDefault(p => p.UserId != dto.SenderId)?
                                                    .User? // Access User property for its Username
                                                    .Username ?? "Unknown User";

                    var senderNotification = createdMessage.ToNewMessageNotificationDTO(
                        chat.IsGroup ? chat.Name ?? "Group Chat" : otherParticipantUsername,
                        chat.AvatarUrl ?? "", // Provide a default empty string if AvatarUrl can be null
                        unreadCountInChatForSender,
                        totalUnreadCountForSender
                    );

                    foreach (var connectionId in senderConnections)
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveNewMessage", messageDTO);
                        await _hubContext.Clients.Client(connectionId).SendAsync("NewMessageNotification", senderNotification);
                    }
                }

                return Result<MessageDTO>.Success(messageDTO);
            }
            catch (Exception ex)
            {
                // This is a good place to log the exception with a proper logging framework (e.g., Serilog, NLog)
                Console.WriteLine($"Exception in SendMessageAsync: {ex.ToString()}");
                return Result<MessageDTO>.Failure($"An error occurred while sending the message: {ex.Message}");
            }
        }
        public async Task<int> GetUnreadMessageCountForChatAsync(int chatId, int userId)
        {
            var participant = await _context.ChatParticipants
                .AsNoTracking()
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == userId);

            if (participant == null) return 0;

            var unreadCount = await _context.Messages
                .Where(m => m.ChatId == chatId &&
                            m.SentAt > participant.JoinedAt &&
                            m.SenderId != userId &&
                            (!participant.LastReadMessageId.HasValue || m.Id > participant.LastReadMessageId.Value))
                .CountAsync();

            return unreadCount;
        }
        public async Task<int> GetTotalUnreadMessageCountForUserAsync(int userId)
        {
            var totalUnreadCount = 0;

            var userChatParticipations = await _context.ChatParticipants
                .Where(cp => cp.UserId == userId)
                .Select(cp => new { cp.ChatId, cp.JoinedAt, cp.LastReadMessageId })
                .AsNoTracking()
                .ToListAsync();

            foreach (var chatParticipant in userChatParticipations)
            {
                var unreadCountInChat = await _context.Messages
                    .Where(m => m.ChatId == chatParticipant.ChatId &&
                                m.SentAt > chatParticipant.JoinedAt &&
                                (!chatParticipant.LastReadMessageId.HasValue || m.Id > chatParticipant.LastReadMessageId.Value))
                    .CountAsync();
                totalUnreadCount += unreadCountInChat;
            }

            return totalUnreadCount;
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

                // Optional: Notify other users or perform further real-time operations in the hub (if desired)

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

                // Optional: Logic to remove user from SignalR group can be handled in the hub if connection exists

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while removing participant: {ex.Message}");
            }
        }
        public async Task<Result<List<int>>> GetUserChatIdsAsync(int userId)
        {
            try
            {
                var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
                if (!userExists)
                {
                    return Result<List<int>>.Failure("User not found.");
                }

                var chatIds = await _context.ChatParticipants
                    .Where(cp => cp.UserId == userId)
                    .Select(cp => cp.ChatId)
                    .Distinct()
                    .ToListAsync();

                return Result<List<int>>.Success(chatIds);
            }
            catch (Exception ex)
            {
                return Result<List<int>>.Failure($"Failed to retrieve user chat IDs: {ex.Message}");
            }
        }
        public async Task<Result<bool>> MarkMessagesAsReadAsync(int chatId, int userId, int lastReadMessageId)
        {
            try
            {
                var participant = await _context.ChatParticipants
                    .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == userId);

                if (participant == null)
                {
                    return Result<bool>.Failure("Chat participant not found.");
                }
                if (!participant.LastReadMessageId.HasValue || lastReadMessageId > participant.LastReadMessageId.Value)
                {
                    participant.LastReadMessageId = lastReadMessageId;
                    participant.LastReadAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in MarkMessagesAsReadAsync: {ex}"); // Log the full exception
                return Result<bool>.Failure($"An error occurred while marking messages as read: {ex.Message}");
            }
        }
        public async Task<Result<List<ChatDTO>>> SearchChatsByNameAsync(string searchTerm, int userId, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return Result<List<ChatDTO>>.Success(new List<ChatDTO>()); // Return empty list if no search term
                }

                var lowerSearchTerm = searchTerm.ToLower();

                var query = _context.Chats
                    .AsNoTracking()
                    // Filter chats where the current user is a participant
                    .Where(c => c.Participants.Any(p => p.UserId == userId));

                var filteredChats = await query
                    .Where(c =>
                        (c.IsGroup && c.Name != null && c.Name.ToLower().Contains(lowerSearchTerm)) || // Search group chat name
                        (!c.IsGroup && c.Participants.Any(p =>
                            p.UserId != userId && // Exclude the current user's participant entry
                            p.User != null && p.User.Username.ToLower().Contains(lowerSearchTerm) // Search other participant's username
                        ))
                    )
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted)) // Load only non-deleted messages
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Reactions)
                    .OrderByDescending(c => c.CreatedAt) // Order by creation date initially
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var chatDTOs = new List<ChatDTO>();

                foreach (var chat in filteredChats)
                {
                    // Utilize the ToDTO mapping extension (which handles the one-to-one name logic)
                    var chatDto = chat.ToDTO(userId);

                    chatDTOs.Add(chatDto);
                }

                // Final ordering by latest activity date, as done in GetUserChatsAsync
                chatDTOs = chatDTOs
                    .OrderByDescending(dto => dto.LastMessage?.SentAt ?? dto.CreatedAt)
                    .ToList();

                return Result<List<ChatDTO>>.Success(chatDTOs);
            }
            catch (Exception ex)
            {
                // Log the full exception for debugging
                Console.WriteLine($"Exception in SearchChatsByNameAsync: {ex}");
                return Result<List<ChatDTO>>.Failure($"An error occurred while searching for chats: {ex.Message}");
            }
        }
        public async Task<Result<bool>> UpdateChatStatusAsync(UpdateChatStatusDTO dto) 
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                if (chat == null)
                {
                    return Result<bool>.Failure("Chat not found."); // <--- CHANGED
                }

                // 1. Basic Validations
                if (chat.IsGroup)
                {
                    return Result<bool>.Failure("Chat status can only be changed for one-on-one chats."); // <--- CHANGED
                }

                if (chat.Status != ChatStatus.Pending)
                {
                    return Result<bool>.Failure($"Chat is not in a '{ChatStatus.Pending}' state. Current status: {chat.Status}."); // <--- CHANGED
                }

                if (dto.NewStatus != ChatStatus.Active && dto.NewStatus != ChatStatus.Rejected)
                {
                    return Result<bool>.Failure("Invalid new status. Only 'Active' or 'Rejected' are allowed for updates."); // <--- CHANGED
                }

                // 2. Authorization: Ensure the user updating the status is the *recipient* of the invitation
                var creatorId = chat.Participants.FirstOrDefault(p => p.IsAdmin)?.UserId;
                if (!creatorId.HasValue)
                {
                    creatorId = chat.Participants.OrderBy(p => p.JoinedAt).FirstOrDefault()?.UserId;
                }

                if (dto.UserId == creatorId)
                {
                    return Result<bool>.Failure("Only the recipient of the chat invitation can accept or reject it."); 
                }

                if (!chat.Participants.Any(p => p.UserId == dto.UserId))
                {
                    return Result<bool>.Failure("User is not a participant in this chat."); // <--- CHANGED
                }

                // 3. Update Status
                chat.Status = dto.NewStatus;
                await _context.SaveChangesAsync();

                // Get the updated DTO to send via SignalR
                var updatedChatDto = chat.ToDTO(dto.UserId);

                string notificationMessage = string.Empty;
                if (dto.NewStatus == ChatStatus.Active)
                {
                    var accepter = chat.Participants.FirstOrDefault(p => p.UserId == dto.UserId)?.User;
                    notificationMessage = $"{accepter?.Username ?? "The user"} accepted the chat invitation.";
                }
                else if (dto.NewStatus == ChatStatus.Rejected)
                {
                    var rejecter = chat.Participants.FirstOrDefault(p => p.UserId == dto.UserId)?.User;
                    notificationMessage = $"{rejecter?.Username ?? "The user"} rejected the chat invitation. This chat is now blocked.";
                }

                foreach (var participant in chat.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        // Send updated chat object (with new status)
                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatStatusUpdated", updatedChatDto);

                        // Send a system message about the action
                        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveSystemMessage", new
                        {
                            ChatId = chat.Id,
                            Content = notificationMessage,
                            SentAt = DateTime.UtcNow,
                            IsSystemMessage = true
                        });
                    }
                }

                return Result<bool>.Success(true); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateChatStatusAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<bool>.Failure($"An error occurred while updating chat status: {ex.Message}"); 
            }
        }
    }
}    