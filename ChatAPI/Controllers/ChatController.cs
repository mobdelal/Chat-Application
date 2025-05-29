using Application.ChatSR;
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
        public async Task<IActionResult> SearchChats(
            [FromQuery] string searchTerm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
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
    }
}

