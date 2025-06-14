﻿using Application.ChatSR;
using Application.UserSr;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
using DTOs.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChatAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IChatService _chatService;
        public ChatController(IUserService userService, IChatService chatService)
        {
            _userService = userService;
            _chatService = chatService;
        }
        [HttpGet("user-chats")]
        public async Task<IActionResult> GetUserChats([FromQuery] int userId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _chatService.GetUserChatsAsync(userId, pageNumber, pageSize);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpPost("create")]
        public async Task<IActionResult> CreateChat([FromForm] CreateChatDTO dto)
        {
            var result = await _chatService.CreateChatAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpGet("{chatId}/messages")] 
        public async Task<IActionResult> GetChatMessages(int chatId, [FromQuery] int? lastMessageId = null, [FromQuery] int pageSize = 50)
        {
            var result = await _chatService.GetChatMessagesAsync(chatId, lastMessageId, pageSize);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpGet("{chatId}/forUser/{userId}")] 
        public async Task<IActionResult> GetChatById(int chatId, int userId)
        {

            var result = await _chatService.GetChatByIdAsync(chatId, userId); 

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromForm] SendMessageDTO dto)
        {
            var result = await _chatService.SendMessageAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        [HttpPost("markasread")]
        [Authorize]
        public async Task<IActionResult> MarkMessagesAsRead([FromBody] MarkAsReadDto markAsReadDto)
        {
            var userIdClaim = User.FindFirst("id"); 
            if (userIdClaim == null)
            {
                return Unauthorized(Result<bool>.Failure("User not authenticated."));
            }

            if (!int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return BadRequest(Result<bool>.Failure("Invalid user ID format."));
            }

            var result = await _chatService.MarkMessagesAsReadAsync(
                markAsReadDto.ChatId,
                currentUserId,
                markAsReadDto.LastReadMessageId
            );

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpGet("search")]
        [Authorize] 
        public async Task<IActionResult> SearchChats( [FromQuery] string searchTerm, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null)
            {
                return Unauthorized(Result<List<ChatDTO>>.Failure("User not authenticated."));
            }

            if (!int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return BadRequest(Result<List<ChatDTO>>.Failure("Invalid user ID format."));
            }

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return Ok(Result<List<ChatDTO>>.Success(new List<ChatDTO>()));
            }

            var result = await _chatService.SearchChatsByNameAsync(searchTerm, currentUserId, pageNumber, pageSize);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPut("{chatId}/status")]
        public async Task<IActionResult> UpdateChatStatus(int chatId, [FromBody] UpdateChatStatusDTO dto)
        {
            if (chatId != dto.ChatId)
            {
                return BadRequest("ChatId in route must match ChatId in body.");
            }

            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized("User ID not found in token.");
            }

            dto.UserId = userId;

            var result = await _chatService.UpdateChatStatusAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result); 
            }
            else
            {
                return BadRequest(result);
            }
        }

        [HttpPut("{chatId}/update")] 
        [Authorize] 
        public async Task<IActionResult> UpdateChat([FromRoute] int chatId, [FromForm] UpdateChatDTO dto)
        {
            if (chatId != dto.ChatId)
            {
                return BadRequest(Result<ChatDTO>.Failure("Chat ID in route must match Chat ID in the request body."));
            }

            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null)
            {
                return Unauthorized(Result<ChatDTO>.Failure("User not authenticated."));
            }

            if (!int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return BadRequest(Result<ChatDTO>.Failure("Invalid user ID format."));
            }

            dto.UserId = currentUserId;

            var result = await _chatService.UpdateChatAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpDelete("{chatId}/participants")]
        [Authorize]

        public async Task<IActionResult> RemoveParticipant(int chatId, [FromBody] RemoveParticipantRequest request)
        {
            var kickerUserIdClaim = User.FindFirst("id");
            if (kickerUserIdClaim == null || !int.TryParse(kickerUserIdClaim.Value, out int kickerUserId))
            {
                return Unauthorized("User is not authenticated or user ID is not available.");
            }

            var result = await _chatService.RemoveParticipantAsync(chatId, request.UserIdToRemove, kickerUserId);

            if (result.IsSuccess)
            {
                return Ok(result); 
            }
            else
            {
                return BadRequest(result); 
            }
        }

        [HttpPost("{chatId}/participants")] 
        [Authorize] 
        public async Task<IActionResult> AddParticipant(int chatId, [FromBody] AddParticipantRequest request)
        {
            var adderUserIdClaim = User.FindFirst("id"); 
            if (adderUserIdClaim == null || !int.TryParse(adderUserIdClaim.Value, out int adderUserId))
            {
                return Unauthorized(Result<ChatDTO>.Failure("User is not authenticated or user ID is invalid."));
            }

            var result = await _chatService.AddParticipantAsync(
                chatId,
                request.UserIdToAdd,
                adderUserId
            );

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }


        [HttpPost("toggle-mute")]
        [Authorize] 
        public async Task<IActionResult> ToggleMuteStatus([FromBody] ToggleMuteStatusDTO dto)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<bool>.Failure("User not authenticated or invalid user ID format."));
            }

            if (dto.UserId != currentUserId)
            {
                return Unauthorized(Result<bool>.Failure("Unauthorized: You can only toggle your own mute status."));
            }

            var result = await _chatService.ToggleMuteStatusAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result); 
        }

        [HttpPost("leave-group")]
        [Authorize] 
        public async Task<IActionResult> LeaveGroupChat([FromBody] LeaveGroupChatDTO dto)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<bool>.Failure("User not authenticated or invalid user ID format."));
            }

            if (dto.UserId != currentUserId)
            {
                return Unauthorized(Result<bool>.Failure("Unauthorized: You can only leave a group as yourself."));
            }

            var result = await _chatService.LeaveGroupChatAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result); 
        }
        [HttpDelete("delete-chat")]
        [Authorize]
        public async Task<IActionResult> DeleteChat([FromBody] DeleteChatDTO dto)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<bool>.Failure("User not authenticated or invalid user ID format."));
            }

            if (dto.RequestingUserId != currentUserId)
            {
                return Unauthorized(Result<bool>.Failure("Unauthorized: You can only delete chats as yourself."));
            }

            var result = await _chatService.DeleteChatAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete("messages/{messageId}")]
        public async Task<IActionResult> DeleteMessage(int messageId, [FromQuery] int chatId) 
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<bool>.Failure("User not authenticated or invalid user ID format."));
            }

            var dto = new DeleteMessageDTO
            {
                MessageId = messageId,
                ChatId = chatId,
                UserId = currentUserId 
            };

            var result = await _chatService.DeleteMessageAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpPut("messages/{messageId}")] 
        public async Task<IActionResult> EditMessage(int messageId, [FromBody] EditMessageDTO dto)
        {
            if (messageId != dto.MessageId)
            {
                return BadRequest(Result<MessageDTO>.Failure("Message ID in route must match Message ID in the request body."));
            }

            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<MessageDTO>.Failure("User not authenticated or invalid user ID format."));
            }

            dto.UserId = currentUserId;

            var result = await _chatService.EditMessageAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }


        [HttpPost("messages/{messageId}/reactions")]
        public async Task<IActionResult> AddReaction(int messageId, [FromBody] AddReactionDTO dto)
        {
            if (messageId != dto.MessageId)
            {
                return BadRequest(Result<MessageDTO>.Failure("Message ID in route must match body."));
            }

            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<MessageDTO>.Failure("Invalid user ID."));
            }

            if (dto.UserId != currentUserId)
            {
                return Unauthorized(Result<MessageDTO>.Failure("You can only add reactions as yourself."));
            }

            var result = await _chatService.AddReactionAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [HttpDelete("messages/{messageId}/reactions")]
        public async Task<IActionResult> RemoveReaction(int messageId, [FromQuery] int chatId, [FromQuery] string reaction)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int currentUserId))
            {
                return Unauthorized(Result<MessageDTO>.Failure("Invalid user ID."));
            }

            var dto = new RemoveReactionDTO
            {
                MessageId = messageId,
                ChatId = chatId,
                UserId = currentUserId,
                Reaction = reaction
            };

            var result = await _chatService.RemoveReactionAsync(dto);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}