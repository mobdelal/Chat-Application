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
                else if (dto.IsGroup)
                {
                    avatarUrl = "/images/default/groupImage.png";
                }

                var chat = new Chat
                {
                    Name = dto.IsGroup ? dto.Name : string.Empty,
                    IsGroup = dto.IsGroup,
                    CreatedAt = now,
                    AvatarUrl = avatarUrl,
                    Participants = new List<ChatParticipant>()
                };

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
                    .FirstOrDefaultAsync(c => c.Id == chat.Id);

                if (createdChat == null)
                    return Result<ChatDTO>.Failure("Failed to load created chat.");

                foreach (var participant in createdChat.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        await _hubContext.Groups.AddToGroupAsync(connectionId, $"Chat-{createdChat.Id}");
                        // Pass participant.UserId as the currentUserId for the DTO being sent
                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatCreated", createdChat.ToDTO(participant.UserId));
                    }
                }

                // For the result, you can use the createdByUserId as the current user context
                return Result<ChatDTO>.Success(createdChat.ToDTO(dto.CreatedByUserId));
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
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .Include(m => m.Reactions)
                    .ToListAsync();

                messages.Reverse(); // Keep messages in chronological order for UI

                var messageDTOs = messages.Select(m => m.ToDTO()).ToList();

                return Result<List<MessageDTO>>.Success(messageDTOs);
            }
            catch (Exception ex)
            {
                return Result<List<MessageDTO>>.Failure($"Failed to load chat messages: {ex.Message}");
            }
        }

        public async Task<Result<ChatDTO>> GetChatByIdAsync(int chatId, int currentUserId)
        {
            try
            {
                // Retrieve the chat with all necessary related data
                // We use AsTracking() here because we intend to modify the ChatParticipant later.
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.OrderBy(m => m.SentAt).Take(50)) // Load initial 50 messages
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

                // Find the current user's participant record
                var currentUserParticipant = chat.Participants.FirstOrDefault(p => p.UserId == currentUserId);

                if (currentUserParticipant == null)
                {
                    return Result<ChatDTO>.Failure("You are not a participant of this chat.");
                }

                if (currentUserParticipant.LastReadAt == null || currentUserParticipant.LastReadAt < DateTime.UtcNow)
                {
                    currentUserParticipant.LastReadAt = DateTime.UtcNow;
                    _context.ChatParticipants.Update(currentUserParticipant); // Mark for update
                    await _context.SaveChangesAsync(); // Persist the change to the database
                }

                var chatDTO = chat.ToDTO(currentUserId);

                return Result<ChatDTO>.Success(chatDTO);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetChatByIdAsync: {ex}"); // Log the full exception
                return Result<ChatDTO>.Failure($"An error occurred while fetching the chat: {ex.Message}");
            }
        }
        public async Task<Result<List<ChatDTO>>> GetUserChatsAsync(int userId, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                // Step 1: Query the chats with all necessary related data using Includes.
                // This ensures all the data needed for DTO mapping is loaded efficiently.
                var chats = await _context.Chats
                    .AsNoTracking() // Optimize for read-only query
                    .Where(c => c.Participants.Any(p => p.UserId == userId))
                    .Include(c => c.Participants) // Include participants
                        .ThenInclude(p => p.User) // Then include user details for participants

                    // Include Messages with their Sender, Attachments, and Reactions.
                    // We're loading ALL messages here that are related to the chats being fetched.
                    // IMPORTANT: This can still be a LOT of data if chats have many messages.
                    // We'll address the 'last message' and 'unread count' in memory for performance.
                    .Include(c => c.Messages.Where(m => !m.IsDeleted)) // Load only non-deleted messages
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Reactions)
                    // If you want to order by last message for initial pagination, it gets complex.
                    // For simplicity, we'll order by CreatedAt for pagination and then re-sort DTOs.
                    .OrderByDescending(c => c.CreatedAt) // Order chats by creation for initial pagination
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(); // Execute query and bring data into memory

                // Step 2: Perform client-side mapping to DTOs.
                // All data (chat, participants, messages, their relations) is now in memory.
                var chatDTOs = new List<ChatDTO>();

                foreach (var chat in chats)
                {
                    var currentUserParticipant = chat.Participants
                        .FirstOrDefault(p => p.UserId == userId);

                    // Calculate UnreadCount
                    var unreadCount = chat.Messages
                        .Count(m => (currentUserParticipant?.LastReadAt == null || m.SentAt > currentUserParticipant.LastReadAt)
                                     && m.SenderId != userId);

                    // Get LastMessage
                    var lastMessage = chat.Messages
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefault(); // This message entity should have its sender, attachments, reactions loaded

                    // Create ChatDTO
                    var dto = new ChatDTO
                    {
                        Id = chat.Id,
                        Name = chat.Name,
                        AvatarUrl = chat.AvatarUrl,
                        IsGroup = chat.IsGroup,
                        CreatedAt = chat.CreatedAt,
                        Participants = chat.Participants?.Select(p => new ChatParticipantDTO
                        {
                            UserId = p.UserId,
                            Username = p.User?.Username ?? "", // Ensure p.User is not null
                            AvatarUrl = p.User?.AvatarUrl,
                            IsAdmin = p.IsAdmin,
                            JoinedAt = p.JoinedAt
                        }).ToList() ?? new(),
                        Messages = new List<MessageDTO>(), // Always empty for chat list view DTO
                        UnreadCount = unreadCount,
                        LastMessage = lastMessage?.ToDTO() // Map the last message entity to its DTO
                    };
                    chatDTOs.Add(dto);
                }

                // Step 3: Re-sort the DTOs by LastMessage.SentAt for frontend display.
                // This re-sorts the already paginated list based on most recent activity.
                chatDTOs = chatDTOs
                    .OrderByDescending(dto => dto.LastMessage?.SentAt ?? dto.CreatedAt)
                    .ToList();

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
    }
}