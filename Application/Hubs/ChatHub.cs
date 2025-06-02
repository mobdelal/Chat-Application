using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Application.ChatSR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DTOs.Shared;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using Application.UserSr;
using DTOs.UserDTOs; 


namespace Application.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IConnectionMappingService _connectionMappingService;
        private readonly IChatService _chatService;
        private readonly IUserService _userService;

        // ***** THIS IS THE CRITICAL FIX *****
        public ChatHub(IChatService chatService, IConnectionMappingService connectionMappingService, IUserService userService)
        {
            _chatService = chatService;
            _connectionMappingService = connectionMappingService;
            _userService = userService;
        }

        public async Task SendMessage(SendMessageDTO dto)
        {
            try
            {
                var result = await _chatService.SendMessageAsync(dto);

                if (!result.IsSuccess || result.Data == null)
                {
                    Console.WriteLine($"Message sending failed: {result.ErrorMessage}");
                    await Clients.Caller.SendAsync("ReceiveMessageError", result.ErrorMessage);
                    return;
                }

                var messageDto = result.Data;

                await Clients.Group($"Chat-{dto.ChatId}")
                    .SendAsync("ReceiveMessage", messageDto);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while sending message: {ex.Message}");
                await Clients.Caller.SendAsync("ReceiveMessageError", "An unexpected error occurred.");
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var userIdString = Context.User?.FindFirst("id")?.Value;

                if (!string.IsNullOrEmpty(userIdString) && int.TryParse(userIdString, out int userId))
                {
                    _connectionMappingService.AddConnection(userId, Context.ConnectionId);
                    Console.WriteLine($"User {userId} connected with ConnectionId: {Context.ConnectionId}");

                    var updateStatusResult = await _userService.UpdateUserStatusAsync(userId, true);
                    if (updateStatusResult.IsSuccess)
                    {
                        Console.WriteLine($"User {userId} is now online.");
                        await Clients.All.SendAsync("UserStatusUpdated", new UserStatusDTO { UserId = userId, IsOnline = true });
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Failed to update online status for user {userId}: {updateStatusResult.ErrorMessage}");
                    }

                    var userChatIdsResult = await _chatService.GetUserChatIdsAsync(userId);

                    if (userChatIdsResult.IsSuccess && userChatIdsResult.Data != null)
                    {
                        foreach (var chatId in userChatIdsResult.Data)
                        {
                            await Groups.AddToGroupAsync(Context.ConnectionId, $"Chat-{chatId}");
                            Console.WriteLine($"Client {Context.ConnectionId} automatically joined chat group Chat-{chatId}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not retrieve chat IDs for User {userId} to join groups. Error: {userChatIdsResult.ErrorMessage}");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Connected client {Context.ConnectionId} has no 'id' claim or it's invalid. Aborting connection.");
                    Context.Abort();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during OnConnectedAsync for ConnectionId {Context.ConnectionId}: {ex.Message}");
                Context.Abort();
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;
            int userId = 0;

            try
            {
                userId = _connectionMappingService.GetUserId(connectionId);

                if (userId != 0)
                {
                    _connectionMappingService.RemoveConnection(userId, connectionId);

                    if (!_connectionMappingService.GetConnections(userId).Any())
                    {
                        var updateStatusResult = await _userService.UpdateUserStatusAsync(userId, false);
                        if (updateStatusResult.IsSuccess)
                        {
                            Console.WriteLine($"User {userId} is now offline.");
                            await Clients.All.SendAsync("UserStatusUpdated", new UserStatusDTO { UserId = userId, IsOnline = false });
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Failed to update offline status for user {userId}: {updateStatusResult.ErrorMessage}");
                        }

                        var updateLastSeenResult = await _userService.UpdateLastSeenAsync(userId);
                        if (updateLastSeenResult.IsSuccess)
                        {
                            Console.WriteLine($"User {userId} last seen updated.");
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Failed to update last seen for user {userId}: {updateLastSeenResult.ErrorMessage}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"ConnectionId {connectionId} disconnected, but UserId not found in mapping. Reason: {exception?.Message}");
                }

                if (exception != null)
                {
                    Console.WriteLine($"Exception during disconnection for ConnectionId {connectionId}: {exception.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during OnDisconnectedAsync cleanup for ConnectionId {connectionId} (User {userId}): {ex.Message}");
            }

            await base.OnDisconnectedAsync(exception);
        }



    }

}