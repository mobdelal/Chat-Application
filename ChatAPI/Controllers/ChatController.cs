using Application.ChatSR;
using Application.UserSr;
using DTOs.ChatDTOs;
using DTOs.MessageDTOs;
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



    }
}

