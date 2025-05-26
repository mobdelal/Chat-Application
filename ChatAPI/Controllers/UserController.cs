using Application.UserSr;
using DTOs.ChatDTOs;
using DTOs.Shared;
using DTOs.UserDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChatAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        public UserController(IUserService userService)
        {
            _userService = userService;
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] RegisterDTO dto)
        {
            var result = await _userService.RegisterAsync(dto);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserDetails(int id)
        {
            var result = await _userService.GetUserDetailsByIdAsync(id);

            if (!result.IsSuccess)
                return NotFound(result);

            return Ok(result);
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDTO dto)
        {
            var result = await _userService.LoginAsync(dto);

            if (!result.IsSuccess)
                return Unauthorized(result);

            return Ok(result);
        }
        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers(
            [FromQuery] string searchTerm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {

            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null)
            {
                return Unauthorized(Result<List<UserDTO>>.Failure("User not authenticated."));
            }

            if (!int.TryParse(userIdClaim.Value, out int searchingUserId))
            {
                return BadRequest(Result<List<UserDTO>>.Failure("Invalid user ID format."));
            }


            var result = await _userService.SearchUsersAsync(searchingUserId, searchTerm, pageNumber, pageSize);

            if (!result.IsSuccess)
            {
                return BadRequest(result); 
            }

            return Ok(result);
        }
        [Authorize] 
        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<bool>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.ChangeUserPasswordAsync(userId, dto);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result); 
        }

        [Authorize] 
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserDTO dto)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<bool>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.UpdateUserProfileAsync(userId, dto);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}