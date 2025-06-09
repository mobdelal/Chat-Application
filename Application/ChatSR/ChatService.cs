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

                // --- ADIT: One-to-one chat specific logic and blocking checks ---
                if (!dto.IsGroup)
                {
                    // Ensure exactly two participants for a one-to-one chat
                    if (allParticipantIds.Count != 2)
                    {
                        return Result<ChatDTO>.Failure("One-to-one chat must have exactly two participants.");
                    }

                    var otherUserId = allParticipantIds.First(id => id != dto.CreatedByUserId);

                    // Check for existing blocks between the two users
                    var isCreatorBlockingOther = await _context.UserBlocks.AnyAsync(ub => ub.BlockerId == dto.CreatedByUserId && ub.BlockedId == otherUserId);
                    var isOtherBlockingCreator = await _context.UserBlocks.AnyAsync(ub => ub.BlockerId == otherUserId && ub.BlockedId == dto.CreatedByUserId);

                    if (isCreatorBlockingOther)
                    {
                        return Result<ChatDTO>.Failure("You have blocked this user. Cannot create a chat.");
                    }
                    if (isOtherBlockingCreator)
                    {
                        return Result<ChatDTO>.Failure("This user has blocked you. Cannot create a chat.");
                    }

                    var existingChat = await _context.Chats
                        .Include(c => c.Participants)
                        .Where(c => !c.IsGroup &&
                                     c.Participants.Any(p => p.UserId == dto.CreatedByUserId) &&
                                     c.Participants.Any(p => p.UserId == otherUserId))
                        .FirstOrDefaultAsync();

                    if (existingChat != null)
                    {
                        // Fetch blocked user IDs here for the existing chat DTO
                        var blockedUserIdsForExistingChat = await _context.UserBlocks
                            .Where(ub => ub.BlockerId == dto.CreatedByUserId || ub.BlockedId == dto.CreatedByUserId ||
                                         ub.BlockerId == otherUserId || ub.BlockedId == otherUserId)
                            .Select(ub => (ub.BlockerId == dto.CreatedByUserId) ? ub.BlockedId : ub.BlockerId) // Get IDs relevant to current user
                            .Distinct()
                            .ToListAsync();

                        if (existingChat.Status == ChatStatus.Active)
                        {
                            return Result<ChatDTO>.Success(existingChat.ToDTO(dto.CreatedByUserId, blockedUserIdsForExistingChat));
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
                // --- End of ADIT ---

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
                    Name = dto.IsGroup ? (dto.Name ?? string.Empty) : string.Empty,
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

                var blockedUserIds = await _context.UserBlocks
                    .Where(ub => ub.BlockerId == dto.CreatedByUserId) 
                    .Select(ub => ub.BlockedId) 
                    .Distinct()
                    .ToListAsync();

                foreach (var participant in createdChat.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        await _hubContext.Groups.AddToGroupAsync(connectionId, $"Chat-{createdChat.Id}");

                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatCreated", createdChat.ToDTO(participant.UserId, blockedUserIds));

                        if (systemMessage != null)
                        {
                            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", systemMessage.ToDTO());
                        }
                    }
                }

                // Pass blockedUserIds to ToDTO for the final return result
                return Result<ChatDTO>.Success(createdChat.ToDTO(dto.CreatedByUserId, blockedUserIds));
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
        public async Task<Result<ChatDTO>> AddParticipantAsync(int chatId, int userIdToAdd, int adderUserId)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User) // Include User for participant DTO mapping
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                    return Result<ChatDTO>.Failure("Chat not found.");

                // --- Admin Authorization Check ---
                var adderParticipant = chat.Participants.FirstOrDefault(p => p.UserId == adderUserId);
                if (adderParticipant == null || !adderParticipant.IsAdmin)
                {
                    return Result<ChatDTO>.Failure("You do not have permission to add participants to this chat.");
                }
                // --- End Admin Authorization Check ---

                var userToAdd = await _context.Users.FindAsync(userIdToAdd);
                if (userToAdd == null)
                    return Result<ChatDTO>.Failure("User to add not found.");

                var isAlreadyParticipant = chat.Participants.Any(p => p.UserId == userIdToAdd);
                if (isAlreadyParticipant)
                    return Result<ChatDTO>.Failure("User is already a participant in this chat.");

                var participant = new ChatParticipant
                {
                    UserId = userIdToAdd,
                    ChatId = chatId,
                    IsAdmin = false, // Newly added participants are typically not admins by default
                    IsMuted = false,
                    JoinedAt = DateTime.UtcNow,
                    IsTyping = false
                };

                chat.Participants.Add(participant);
                await _context.SaveChangesAsync(); // Save participant changes

                var systemMessageContent = $"{userToAdd.Username} has joined the group.";
                var systemMessage = new Message
                {
                    ChatId = chatId,
                    SenderId = adderUserId, // This could also be a dedicated 'System' user ID
                    Content = systemMessageContent,
                    SentAt = DateTime.UtcNow,
                    IsSystemMessage = true
                };

                _context.Messages.Add(systemMessage);
                await _context.SaveChangesAsync(); // Save the system message

                // Re-fetch the updated chat with all its current state (including the new participant and system message's last message)
                // Ensure you include everything needed for the ChatDTO, especially for the newly joined user
                var updatedChat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted)) // Filter for relevant messages, not deleted ones
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages) // Include messages for attachments and reactions if needed for the DTO
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Reactions)
                    // OrderByDescending on messages and then taking FirstOrDefault ensures LastMessage in DTO is correct
                    .AsNoTracking() // Use AsNoTracking if you don't need to track this entity further
                    .FirstOrDefaultAsync(c => c.Id == chatId);


                if (updatedChat == null)
                {
                    Console.WriteLine($"Error: Failed to retrieve updated chat {chatId} after adding participant.");
                    return Result<ChatDTO>.Failure("Failed to retrieve updated chat after adding participant.");
                }

                // Send system message to the chat group (all participants, including the new one, will receive this)
                await _hubContext.Clients.Group($"Chat-{chatId}")
                                         .SendAsync("ReceiveMessage", systemMessage.ToDTO());
                Console.WriteLine($"SignalR: Sent ReceiveMessage to Chat-{chatId} group for system message (user {userIdToAdd} joined).");


                // --- Differentiate notifications for existing members vs. the newly added member ---

                // 1. Notify *existing* participants about the chat update
                foreach (var p in updatedChat.Participants)
                {
                    // Skip the newly added user; they'll get the 'ChatJoined' event
                    if (p.UserId == userIdToAdd) continue;

                    var participantBlockedUserIds = await _context.UserBlocks
                        .Where(ub => ub.BlockerId == p.UserId)
                        .Select(ub => ub.BlockedId)
                        .Distinct()
                        .ToListAsync();

                    var chatDtoForExistingParticipant = updatedChat.ToDTO(p.UserId, participantBlockedUserIds);
                    Console.WriteLine($"SignalR: Sending ChatUpdated for ChatId: {chatId} to existing user {p.UserId}.");
                    await _hubContext.Clients.User(p.UserId.ToString())
                                     .SendAsync("ChatUpdated", chatDtoForExistingParticipant);
                }

                // 2. Handle the newly added user specifically:
                //    - Add them to the SignalR group for future messages.
                //    - Send them the initial chat object via the 'ChatJoined' event.
                var newUserConnections = _connectionMappingService.GetConnections(userIdToAdd);
                if (newUserConnections.Any())
                {
                    var newParticipantBlockedUserIds = await _context.UserBlocks
                        .Where(ub => ub.BlockerId == userIdToAdd)
                        .Select(ub => ub.BlockedId)
                        .Distinct()
                        .ToListAsync();

                    var chatDtoForNewUser = updatedChat.ToDTO(userIdToAdd, newParticipantBlockedUserIds);

                    foreach (var connectionId in newUserConnections)
                    {
                        Console.WriteLine($"SignalR: Instructing new user {userIdToAdd} (connection {connectionId}) to join Chat-{chatId} group.");
                        await _hubContext.Groups.AddToGroupAsync(connectionId, $"Chat-{chatId}");

                        Console.WriteLine($"SignalR: Sending ChatJoined for ChatId: {chatId} to new user {userIdToAdd} on connection {connectionId}.");
                        await _hubContext.Clients.Client(connectionId)
                                         .SendAsync("ChatJoined", chatDtoForNewUser);
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: New user {userIdToAdd} has no active SignalR connections. They might not receive the initial chat immediately via ChatJoined.");
                }

                // Return the DTO for the adding user's immediate UI update (the adder is one of the existing participants)
                var adderBlockedUserIds = await _context.UserBlocks
                    .Where(ub => ub.BlockerId == adderUserId)
                    .Select(ub => ub.BlockedId)
                    .Distinct()
                    .ToListAsync();
                var updatedChatDtoForAdder = updatedChat.ToDTO(adderUserId, adderBlockedUserIds);

                return Result<ChatDTO>.Success(updatedChatDtoForAdder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding participant: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<ChatDTO>.Failure($"An error occurred while adding participant: {ex.Message}");
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

                // --- ADIT: Get the list of people the current user is blocking ---
                var blockedUserIds = await _context.UserBlocks
                    .Where(ub => ub.BlockerId == currentUserId) // Filter for rows where currentUserId is the blocker
                    .Select(ub => ub.BlockedId) // Select only the IDs of the users being blocked
                    .Distinct()
                    .ToListAsync();

                var chatDTO = chat.ToDTO(currentUserId, blockedUserIds);

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

                foreach (var chat in orderedChats)
                {
                    var currentUserParticipant = chat.Participants?
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

                    var createdByUserId = chat.Participants?.FirstOrDefault(p => p.IsAdmin)?.UserId ?? 0;

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
                        // --- ADDITION START ---
                        IsMutedForCurrentUser = currentUserParticipant?.IsMuted ?? false, // Map IsMuted from the current user's participant record
                                                                                          // --- ADDITION END ---
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


                        var fileUrl = await _fileStorageService.SaveFileAsync(
                            fileBytes,
                            attachmentDto.FileName ?? string.Empty, 
                            attachmentDto.FileType ?? "application/octet-stream" 
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
        public async Task<Result<bool>> RemoveParticipantAsync(int chatId, int userIdToRemove, int kickerUserId)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User) // Include User for participant DTO mapping
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (chat == null)
                    return Result<bool>.Failure("Chat not found.");

                var kickerParticipant = chat.Participants.FirstOrDefault(p => p.UserId == kickerUserId);
                if (kickerParticipant == null || !kickerParticipant.IsAdmin)
                    return Result<bool>.Failure("You do not have permission to remove participants.");

                var participantToRemove = chat.Participants.FirstOrDefault(p => p.UserId == userIdToRemove);
                if (participantToRemove == null)
                    return Result<bool>.Failure("User is not a participant in this chat.");

                // Prevent removing the last admin or an admin by a non-admin (if that's a rule)
                if (participantToRemove.IsAdmin && chat.Participants.Count(p => p.IsAdmin) == 1 && kickerParticipant.UserId == userIdToRemove)
                {
                    return Result<bool>.Failure("Cannot remove the last admin from the chat.");
                }
                if (participantToRemove.IsAdmin && !kickerParticipant.IsAdmin) // Only an admin can remove another admin
                {
                    return Result<bool>.Failure("Only an admin can remove another admin.");
                }

                // Store the user's name before removing the participant for the system message
                var removedUser = await _context.Users.FindAsync(userIdToRemove);
                string removedUserName = removedUser?.Username ?? "A user";


                chat.Participants.Remove(participantToRemove);
                await _context.SaveChangesAsync(); // Save the removal of the participant

                // --- Add a system message for the removal ---
                var systemMessageContent = $"{removedUserName} has been removed from the group.";
                var systemMessage = new Message
                {
                    ChatId = chatId,
                    SenderId = kickerUserId, // The user who performed the action
                    Content = systemMessageContent,
                    SentAt = DateTime.UtcNow,
                    IsSystemMessage = true
                };

                _context.Messages.Add(systemMessage);
                await _context.SaveChangesAsync(); // Save the system message

                // Re-fetch the updated chat with its current state (after removal and system message)
                var updatedChat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.Reactions)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == chatId);

                if (updatedChat == null)
                {
                    // This might happen if the chat was just deleted, but we've already checked for null earlier.
                    // For participant removal, it should always exist unless there's a serious bug.
                    Console.WriteLine($"Error: Failed to retrieve updated chat {chatId} after participant removal.");
                    // We can still proceed with notifications even if we can't fully map the DTO for existing members.
                }

                // Send system message to the chat group (all remaining members will see this)
                await _hubContext.Clients.Group($"Chat-{chatId}")
                                         .SendAsync("ReceiveMessage", systemMessage.ToDTO());
                Console.WriteLine($"SignalR: Sent ReceiveMessage to Chat-{chatId} group for system message (user {userIdToRemove} removed).");


                // --- Differentiate notifications for the removed user vs. remaining participants ---

                // 1. Notify the user who was removed (if they have active connections)
                var removedUserConnections = _connectionMappingService.GetConnections(userIdToRemove);
                if (removedUserConnections.Any())
                {
                    foreach (var connectionId in removedUserConnections)
                    {
                        Console.WriteLine($"SignalR: Sending ChatLeft for ChatId: {chatId} to removed user {userIdToRemove} on connection {connectionId}.");
                        // Explicitly remove from SignalR group
                        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, $"Chat-{chatId}");

                        // --- NEW SIGNALR EVENT FOR LEFT CHAT ---
                        // Send the chat ID so the client knows which chat to remove
                        await _hubContext.Clients.Client(connectionId)
                                         .SendAsync("ChatLeft", chatId);
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Removed user {userIdToRemove} has no active SignalR connections. They won't receive the ChatLeft event.");
                }

                // 2. Notify all *remaining* participants about the chat update (removed participant, new last message)
                if (updatedChat != null) // Only proceed if we successfully re-fetched the updated chat
                {
                    foreach (var p in updatedChat.Participants)
                    {
                        // Ensure we don't send ChatUpdated to the user who was just removed (already handled by ChatLeft)
                        // and also not to the current `kickerUserId` as they will get the HTTP response immediately
                        if (p.UserId == userIdToRemove) continue; // Should already be handled above
                                                                  // if (p.UserId == kickerUserId) continue; // The kicker already updated their UI via HTTP response

                        var participantBlockedUserIds = await _context.UserBlocks
                            .Where(ub => ub.BlockerId == p.UserId)
                            .Select(ub => ub.BlockedId)
                            .Distinct()
                            .ToListAsync();

                        var chatDtoForRemainingParticipant = updatedChat.ToDTO(p.UserId, participantBlockedUserIds);
                        Console.WriteLine($"SignalR: Sending ChatUpdated for ChatId: {chatId} to remaining user {p.UserId}.");
                        await _hubContext.Clients.User(p.UserId.ToString())
                                         .SendAsync("ChatUpdated", chatDtoForRemainingParticipant);
                    }
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing participant: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
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
                    .Where(c => c.Participants.Any(p => p.UserId == userId));

                var filteredChats = await query
                    .Where(c =>
                        (c.IsGroup && c.Name != null && c.Name.ToLower().Contains(lowerSearchTerm)) || // Search group chat name
                        (!c.IsGroup && c.Participants.Any(p =>
                            p.UserId != userId && 
                            p.User != null && p.User.Username.ToLower().Contains(lowerSearchTerm) // Search other participant's username
                        ))
                    )
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted)) 
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Reactions)
                    .OrderByDescending(c => c.CreatedAt) 
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var chatDTOs = new List<ChatDTO>();
                var blockedUserIds = await _context.UserBlocks
                    .Where(ub => ub.BlockerId == userId) 
                    .Select(ub => ub.BlockedId) 
                    .Distinct()
                    .ToListAsync();

                foreach (var chat in filteredChats)
                {
                    var chatDto = chat.ToDTO(userId, blockedUserIds);

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
                    return Result<bool>.Failure("Chat not found.");
                }

                // 1. Basic Validations
                if (chat.IsGroup)
                {
                    return Result<bool>.Failure("Chat status can only be changed for one-on-one chats.");
                }

                if (chat.Status != ChatStatus.Pending)
                {
                    return Result<bool>.Failure($"Chat is not in a '{ChatStatus.Pending}' state. Current status: {chat.Status}.");
                }

                if (dto.NewStatus != ChatStatus.Active && dto.NewStatus != ChatStatus.Rejected)
                {
                    return Result<bool>.Failure("Invalid new status. Only 'Active' or 'Rejected' are allowed for updates.");
                }

                var initiatorParticipant = chat.Participants.FirstOrDefault(p => p.IsAdmin);
                if (initiatorParticipant == null)
                {
                    return Result<bool>.Failure("Chat initiator (admin) could not be determined.");
                }
                var initiatorId = initiatorParticipant.UserId;

                // The recipient is the other participant who is not the initiator/admin
                var recipientParticipant = chat.Participants.FirstOrDefault(p => p.UserId != initiatorId);
                if (recipientParticipant == null)
                {
                    return Result<bool>.Failure("Chat recipient could not be determined.");
                }
                var recipientId = recipientParticipant.UserId;

                if (dto.UserId != recipientId)
                {
                    return Result<bool>.Failure("Only the recipient of the chat invitation can accept or reject it.");
                }

                // --- ADIT: Update Status and then fetch blocked user IDs ---
                chat.Status = dto.NewStatus;
                await _context.SaveChangesAsync();

                string notificationMessage = string.Empty;
                if (dto.NewStatus == ChatStatus.Active)
                {
                    notificationMessage = $"{recipientParticipant.User?.Username ?? "The user"} accepted the chat invitation.";
                }
                else if (dto.NewStatus == ChatStatus.Rejected)
                {
                    var rejecterId = dto.UserId;
                    var blockedUserId = initiatorId; // The initiator (admin) is the one being blocked when rejected

                    var existingBlock = await _context.UserBlocks
                        .FirstOrDefaultAsync(b => b.BlockerId == rejecterId && b.BlockedId == blockedUserId);

                    if (existingBlock == null)
                    {
                        var userBlock = new UserBlock
                        {
                            BlockerId = rejecterId,
                            BlockedId = blockedUserId,
                            BlockedAt = DateTime.UtcNow
                        };
                        _context.UserBlocks.Add(userBlock);
                        await _context.SaveChangesAsync();
                    }

                    notificationMessage = $"{recipientParticipant.User?.Username ?? "The user"} rejected the chat invitation and blocked the sender.";
                }

                // --- ADIT: Get the list of people the current user (dto.UserId) is blocking ---
                var blockedUserIds = await _context.UserBlocks
                    .Where(ub => ub.BlockerId == dto.UserId) // Filter for rows where dto.UserId is the blocker
                    .Select(ub => ub.BlockedId) // Select only the IDs of the users being blocked
                    .Distinct()
                    .ToListAsync();

                // --- ADIT: Pass the blockedUserIds list to the ToDTO extension method ---
                var updatedChatDto = chat.ToDTO(dto.UserId, blockedUserIds);

                foreach (var participant in chat.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatStatusUpdated", updatedChatDto);
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
        public async Task<Result<ChatDTO>> UpdateChatAsync(UpdateChatDTO dto)
        {
            try
            {

                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User) 
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                if (chat == null)
                {
                    return Result<ChatDTO>.Failure("Chat not found.");
                }

                if (!chat.IsGroup)
                {
                    return Result<ChatDTO>.Failure("Only group chats can be updated.");
                }

                // 2. Authorization: Check if the provided UserId (from claims) is an admin of this chat
                var adminParticipant = chat.Participants.FirstOrDefault(p => p.UserId == dto.UserId && p.IsAdmin);
                if (adminParticipant == null)
                {
                    return Result<ChatDTO>.Failure("Only chat administrators can update chat details.");
                }

                var adminUser = adminParticipant.User!;

                bool changesMade = false;
                List<Message> systemMessages = new List<Message>();
                var now = DateTime.UtcNow;

                // 3. Update Name if provided and different
                if (!string.IsNullOrWhiteSpace(dto.Name) && chat.Name != dto.Name)
                {
                    string oldName = chat.Name;
                    chat.Name = dto.Name;
                    changesMade = true;

                    // Add system message for name change
                    string systemMessageContent = $"{adminUser.Username} changed the group name from \"{oldName}\" to \"{chat.Name}\".";
                    systemMessages.Add(new Message
                    {
                        ChatId = chat.Id,
                        SenderId = adminUser.Id, // System messages often attributed to the action-taker
                        Content = systemMessageContent,
                        SentAt = now,
                        IsSystemMessage = true
                    });
                }

                // 4. Update Avatar if a new file is provided
                if (dto.AvatarFile != null)
                {
                    // Validate file type
                    var allowedMimeTypes = new[]
                    {
                        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "image/tiff"
                    };
                    var mimeType = dto.AvatarFile.ContentType.ToLower();

                    if (!allowedMimeTypes.Contains(mimeType))
                    {
                        return Result<ChatDTO>.Failure("Invalid file type. Please upload an image file (JPEG, PNG, GIF, BMP, WEBP, TIFF).");
                    }

                    // Delete old avatar if it exists and is not a default image
                    if (!string.IsNullOrEmpty(chat.AvatarUrl) && !chat.AvatarUrl.Contains("/images/default/groupImage.png"))
                    {
                        string oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", chat.AvatarUrl.TrimStart('/'));
                        if (File.Exists(oldFilePath))
                        {
                            try
                            {
                                File.Delete(oldFilePath);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not delete old avatar file {oldFilePath}. Error: {ex.Message}");
                            }
                        }
                    }

                    // Save new avatar
                    var fileName = "chat_" + Guid.NewGuid() + Path.GetExtension(dto.AvatarFile.FileName);
                    string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/chat_avatars");
                    Directory.CreateDirectory(uploadFolder);
                    string filePath = Path.Combine(uploadFolder, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.AvatarFile.CopyToAsync(fileStream);
                    }
                    chat.AvatarUrl = "/images/chat_avatars/" + fileName;
                    changesMade = true;

                    // Add system message for avatar change
                    string systemMessageContentForAvatar = $"{adminUser.Username} updated the group avatar.";
                    systemMessages.Add(new Message
                    {
                        ChatId = chat.Id,
                        SenderId = adminUser.Id,
                        Content = systemMessageContentForAvatar,
                        SentAt = now,
                        IsSystemMessage = true
                    });
                }

                if (!changesMade)
                {
                    return Result<ChatDTO>.Failure("No changes provided to update the chat (name or new avatar file).");
                }

                _context.Messages.AddRange(systemMessages); // Add all generated system messages
                await _context.SaveChangesAsync();

                // 5. Fetch the updated chat with necessary includes for DTO mapping
                var updatedChatEntity = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == chat.Id);

                if (updatedChatEntity == null)
                {
                    return Result<ChatDTO>.Failure("Failed to retrieve updated chat after saving changes.");
                }

                // 6. Notify all participants via SignalR for the system messages and chat update
                var blockedUserIds = await _context.UserBlocks
                    .Where(ub => updatedChatEntity.Participants.Select(p => p.UserId).Contains(ub.BlockerId))
                    .Select(ub => ub.BlockedId)
                    .Distinct()
                    .ToListAsync();

                foreach (var participant in updatedChatEntity.Participants)
                {
                    var connections = _connectionMappingService.GetConnections(participant.UserId);
                    foreach (var connectionId in connections)
                    {
                        // Send the updated ChatDTO so clients can refresh chat list/details if needed
                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatUpdated", updatedChatEntity.ToDTO(participant.UserId, blockedUserIds));

                        // Send each new system message
                        foreach (var sysMsg in systemMessages)
                        {
                            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", sysMsg.ToDTO());
                        }
                    }
                }

                // 7. Return the updated chat DTO
                return Result<ChatDTO>.Success(updatedChatEntity.ToDTO(dto.UserId, blockedUserIds));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateChatAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<ChatDTO>.Failure($"An error occurred while updating the chat: {ex.Message}");
            }
        }
        public async Task<Result<bool>> ToggleMuteStatusAsync(ToggleMuteStatusDTO dto)
        {
            try
            {
                var participant = await _context.ChatParticipants
                    .Include(cp => cp.Chat)
                    .Include(cp => cp.User) 
                    .FirstOrDefaultAsync(cp => cp.ChatId == dto.ChatId && cp.UserId == dto.UserId);

                if (participant == null)
                {
                    return Result<bool>.Failure("Chat participant not found.");
                }

                bool newMuteStatus = !participant.IsMuted;
                participant.IsMuted = newMuteStatus;
                await _context.SaveChangesAsync();


                var updatePayload = new
                {
                    ChatId = dto.ChatId,
                    UserId = dto.UserId,         
                    IsMuted = newMuteStatus,     
                                                 
                };

                var chatGroup = $"Chat-{dto.ChatId}";

                await _hubContext.Clients.Group(chatGroup).SendAsync("ChatMuteStatusUpdated", updatePayload);

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling mute status: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<bool>.Failure($"An error occurred while toggling mute status: {ex.Message}");
            }
        }
        public async Task<Result<bool>> DeleteChatAsync(DeleteChatDTO dto)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                if (chat == null)
                {
                    return Result<bool>.Failure("Chat not found.");
                }

                var creatorParticipant = chat.Participants.FirstOrDefault(p => p.IsAdmin);

                if (creatorParticipant == null || creatorParticipant.UserId != dto.RequestingUserId)
                {
                    if (!chat.IsGroup && chat.Participants.Any(p => p.UserId == dto.RequestingUserId))
                    {
                        return Result<bool>.Failure("Unauthorized: Only the chat creator can delete this private chat.");
                    }
                    else if (chat.IsGroup)
                    {
                        return Result<bool>.Failure("Unauthorized: Only the chat creator can delete this group chat.");
                    }
                    else
                    {
                        return Result<bool>.Failure("Unauthorized: You do not have permission to delete this chat.");
                    }
                }

                var participantIds = chat.Participants.Select(p => p.UserId).ToList();
                var chatGroup = $"Chat-{chat.Id}";

                // Delete associated files (avatars, attachments)
                if (!string.IsNullOrEmpty(chat.AvatarUrl) &&
                    !chat.AvatarUrl.Contains("/images/default/groupImage.png") &&
                    !chat.AvatarUrl.Contains("/images/default/defaultUser.png"))
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", chat.AvatarUrl.TrimStart('/'));
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }

                var messagesWithAttachments = await _context.Messages
                    .Where(m => m.ChatId == chat.Id)
                    .Include(m => m.Attachments)
                    .ToListAsync();

                foreach (var message in messagesWithAttachments)
                {
                    foreach (var attachment in message.Attachments)
                    {
                        if (!string.IsNullOrEmpty(attachment.FileUrl))
                        {
                            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", attachment.FileUrl.TrimStart('/'));
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        }
                    }
                }

                _context.Chats.Remove(chat);
                await _context.SaveChangesAsync();

                foreach (var userId in participantIds)
                {
                    var connections = _connectionMappingService.GetConnections(userId);
                    foreach (var connectionId in connections)
                    {
                        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, chatGroup);
                        await _hubContext.Clients.Client(connectionId).SendAsync("ChatDeleted", dto.ChatId);
                    }
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting chat: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Result<bool>.Failure($"An error occurred while deleting the chat: {ex.Message}");
            }
        }
        public async Task<Result<bool>> LeaveGroupChatAsync(LeaveGroupChatDTO dto)
        {
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                if (chat == null)
                {
                    return Result<bool>.Failure("Chat not found.");
                }

                if (!chat.IsGroup)
                {
                    return Result<bool>.Failure("You can only leave group chats. For private chats, you can block or delete the chat from your view.");
                }

                var participantToRemove = chat.Participants.FirstOrDefault(p => p.UserId == dto.UserId);

                if (participantToRemove == null)
                {
                    return Result<bool>.Failure("You are not a participant of this group chat.");
                }

                var remainingAdmins = chat.Participants
                    .Where(p => p.IsAdmin && p.UserId != dto.UserId)
                    .ToList();

                var otherParticipants = chat.Participants
                    .Where(p => p.UserId != dto.UserId)
                    .ToList();

                if (participantToRemove.IsAdmin && !remainingAdmins.Any() && otherParticipants.Any())
                {
                    var newAdmin = otherParticipants.FirstOrDefault();
                    if (newAdmin != null)
                    {
                        newAdmin.IsAdmin = true;

                        // Optional: Log the promotion as a system message
                        var promotedMessage = new Message
                        {
                            ChatId = dto.ChatId,
                            SenderId = dto.UserId,
                            Content = $"{newAdmin.User?.Username ?? "A member"} has been promoted to admin.",
                            SentAt = DateTime.UtcNow,
                            IsSystemMessage = true
                        };
                        _context.Messages.Add(promotedMessage);
                    }
                }

                _context.ChatParticipants.Remove(participantToRemove);
                await _context.SaveChangesAsync();

                string systemMessageContent = $"{participantToRemove.User?.Username ?? "A user"} has left the group.";
                var systemMessage = new Message
                {
                    ChatId = dto.ChatId,
                    SenderId = dto.UserId,
                    Content = systemMessageContent,
                    SentAt = DateTime.UtcNow,
                    IsSystemMessage = true
                };
                _context.Messages.Add(systemMessage);
                await _context.SaveChangesAsync();

                // 🔁 Check if the group is now empty and delete it
                if (!chat.Participants.Any())
                {
                    var actualRemainingParticipants = await _context.ChatParticipants
                        .CountAsync(cp => cp.ChatId == chat.Id);

                    if (actualRemainingParticipants == 0)
                    {
                        var deleteResult = await DeleteChatAsync(new DeleteChatDTO
                        {
                            ChatId = chat.Id,
                            RequestingUserId = dto.UserId
                        });

                        if (!deleteResult.IsSuccess)
                        {
                            Console.WriteLine($"Warning: Failed to auto-delete empty chat after last participant left: {deleteResult.ErrorMessage}");
                            return Result<bool>.Failure($"You have left the chat, but there was an issue deleting the now-empty chat: {deleteResult.ErrorMessage}");
                        }

                        return Result<bool>.Success(true);
                    }
                }

                var updatedChat = await _context.Chats
                    .Include(c => c.Participants).ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted).OrderByDescending(m => m.SentAt).Take(1)).ThenInclude(m => m.Sender)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == dto.ChatId);

                // 🔁 Notify all affected users
                var allAffectedUserIds = chat.Participants.Select(p => p.UserId).ToList();
                allAffectedUserIds.Add(dto.UserId); // Add leaving user

                foreach (var userId in allAffectedUserIds.Distinct())
                {
                    var connections = _connectionMappingService.GetConnections(userId);
                    foreach (var connectionId in connections)
                    {
                        if (userId == dto.UserId)
                        {
                            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, $"Chat-{dto.ChatId}");
                            await _hubContext.Clients.Client(connectionId).SendAsync("ChatLeft", dto.ChatId);
                        }
                        else if (updatedChat != null)
                        {
                            var participantBlockedUserIds = await _context.UserBlocks
                                .Where(ub => ub.BlockerId == userId)
                                .Select(ub => ub.BlockedId)
                                .Distinct()
                                .ToListAsync();

                            await _hubContext.Clients.Client(connectionId).SendAsync("ChatUpdated", updatedChat.ToDTO(userId, participantBlockedUserIds));
                            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", systemMessage.ToDTO());
                        }
                    }
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error leaving group chat: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return Result<bool>.Failure($"An error occurred while leaving the group chat: {ex.Message}");
            }
        }
        public async Task<Result<bool>> DeleteMessageAsync(DeleteMessageDTO dto)
        {
            try
            {

                var message = await _context.Messages
                    .Include(m => m.Chat) 
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId && m.ChatId == dto.ChatId && !m.IsDeleted);

                if (message == null)
                {
                    return Result<bool>.Failure("Message not found or already deleted.");
                }

                var isSender = message.SenderId == dto.UserId;
                var isChatAdmin = false;
                if (!isSender && message.Chat.IsGroup) 
                {
                    var participant = await _context.ChatParticipants
                        .FirstOrDefaultAsync(p => p.ChatId == dto.ChatId && p.UserId == dto.UserId);
                    isChatAdmin = participant?.IsAdmin ?? false;
                }

                if (!isSender && !isChatAdmin)
                {
                    return Result<bool>.Failure("Unauthorized to delete this message.");
                }

                // 4. Perform Soft Delete
                message.IsDeleted = true;

                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group($"Chat-{dto.ChatId}")
                    .SendAsync("MessageDeleted", dto.MessageId, dto.ChatId);

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while deleting the message: {ex.Message}");

            }
        }
        public async Task<Result<MessageDTO>> EditMessageAsync(EditMessageDTO dto)
        {
            try
            {

                var message = await _context.Messages
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments) 
                    .Include(m => m.Reactions)
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId && m.ChatId == dto.ChatId && !m.IsDeleted);

                if (message == null)
                {
                    return Result<MessageDTO>.Failure("Message not found or already deleted.");
                }

                if (message.SenderId != dto.UserId)
                {
                    return Result<MessageDTO>.Failure("Unauthorized to edit this message.");
                }

                message.Content = dto.NewContent;
                message.EditedAt = DateTime.UtcNow; 
                message. isEdited= true; 

                await _context.SaveChangesAsync();

                // 6. Map to DTO
                var updatedMessageDTO = message.ToDTO();

                await _hubContext.Clients.Group($"Chat-{dto.ChatId}") 
                    .SendAsync("MessageEdited", updatedMessageDTO);

                return Result<MessageDTO>.Success(updatedMessageDTO);
            }
            catch (Exception ex)
            {
                return Result<MessageDTO>.Failure($"An error occurred while editing the message: {ex.Message}");
            }
        }
        public async Task<Result<MessageDTO>> AddReactionAsync(AddReactionDTO dto)
        {
            try
            {
                var message = await _context.Messages
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments) // Include attachments for the ToDTO mapping
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId && m.ChatId == dto.ChatId);

                if (message == null)
                {
                    return Result<MessageDTO>.Failure("Message not found.");
                }

                // Check if the user already has ANY reaction on this message
                var existingUserReaction = message.Reactions
                    .FirstOrDefault(r => r.UserId == dto.UserId);

                if (existingUserReaction != null)
                {
                    // If the existing reaction is the SAME as the new one, it means they clicked to "un-react"
                    if (existingUserReaction.Reaction == dto.Reaction)
                    {
                        // Remove the existing reaction
                        message.Reactions.Remove(existingUserReaction);
                        _context.MessageReactions.Remove(existingUserReaction); // Mark for deletion in DB
                        await _context.SaveChangesAsync();

                        // Re-fetch and broadcast updated message
                        var updatedMessageAfterRemoval = await _context.Messages
                            .Include(m => m.Reactions)
                                .ThenInclude(r => r.User)
                            .Include(m => m.Sender)
                            .Include(m => m.Attachments)
                            .FirstOrDefaultAsync(m => m.Id == dto.MessageId);

                        if (updatedMessageAfterRemoval == null)
                        {
                            return Result<MessageDTO>.Failure("Failed to retrieve updated message after removing reaction.");
                        }

                        var updatedMessageDTOAfterRemoval = updatedMessageAfterRemoval.ToDTO();
                        await _hubContext.Clients.Group($"Chat-{dto.ChatId}")
                            .SendAsync("MessageUpdated", updatedMessageDTOAfterRemoval); // Use MessageUpdated
                        return Result<MessageDTO>.Success(updatedMessageDTOAfterRemoval);
                    }
                    else // User has a different reaction, remove the old one and add the new one
                    {
                        message.Reactions.Remove(existingUserReaction);
                        _context.MessageReactions.Remove(existingUserReaction); // Mark for deletion
                        // No SaveChanges yet, we'll save after adding the new one
                    }
                }

                // Add the new reaction (this will happen if no existing reaction, or if a different one was removed)
                var user = await _context.Users.FindAsync(dto.UserId);
                if (user == null)
                {
                    return Result<MessageDTO>.Failure("User not found.");
                }

                var newReaction = new MessageReaction
                {
                    MessageId = dto.MessageId,
                    UserId = dto.UserId,
                    Reaction = dto.Reaction,
                    User = user // Attach the user entity
                };

                message.Reactions.Add(newReaction);
                await _context.SaveChangesAsync(); // Save changes for both removal (if any) and addition

                // Re-fetch the message to ensure all includes are fresh before mapping
                var updatedMessage = await _context.Messages
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId);

                if (updatedMessage == null)
                {
                    return Result<MessageDTO>.Failure("Failed to retrieve updated message after adding reaction.");
                }

                var updatedMessageDTO = updatedMessage.ToDTO();


                await _hubContext.Clients.Group($"Chat-{dto.ChatId}")
                     .SendAsync("MessageReactionAdded", updatedMessageDTO);

                return Result<MessageDTO>.Success(updatedMessageDTO);
            }
            catch (Exception ex)
            {
                return Result<MessageDTO>.Failure($"An error occurred while adding reaction: {ex.Message}");
            }
        }
        public async Task<Result<MessageDTO>> RemoveReactionAsync(RemoveReactionDTO dto)
        {
            try
            {
                var message = await _context.Messages
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Sender) 
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId && m.ChatId == dto.ChatId);

                if (message == null)
                {
                    return Result<MessageDTO>.Failure("Message not found.");
                }

                var reactionToRemove = message.Reactions
                    .FirstOrDefault(r => r.UserId == dto.UserId && r.Reaction == dto.Reaction);

                if (reactionToRemove == null)
                {
                    return Result<MessageDTO>.Failure("Reaction not found on this message by this user.");
                }

                message.Reactions.Remove(reactionToRemove);
                _context.MessageReactions.Remove(reactionToRemove); 
                await _context.SaveChangesAsync();

                // Re-fetch the message to ensure all includes are fresh before mapping
                var updatedMessage = await _context.Messages
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Sender)
                    .Include(m => m.Attachments)
                    .FirstOrDefaultAsync(m => m.Id == dto.MessageId);

                if (updatedMessage == null)
                {
                    return Result<MessageDTO>.Failure("Failed to retrieve updated message after removing reaction.");
                }

                var updatedMessageDTO = updatedMessage.ToDTO();

                await _hubContext.Clients.Group($"Chat-{dto.ChatId}")
                    .SendAsync("MessageReactionRemoved", updatedMessageDTO);

                return Result<MessageDTO>.Success(updatedMessageDTO);
            }
            catch (Exception ex)
            {
                return Result<MessageDTO>.Failure($"An error occurred while removing reaction: {ex.Message}");
            }
        }
    }
}