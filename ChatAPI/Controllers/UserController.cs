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

        [Authorize] 
        [HttpGet("me")] 
        public async Task<IActionResult> GetCurrentUserDetails()
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {

                return Unauthorized(Result<UserDetailsDTO>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.GetUserDetailsByIdAsync(userId); // Call your service with the token's userId

            if (!result.IsSuccess)
            {
                return NotFound(result);
            }

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
        public async Task<IActionResult> UpdateProfile([FromForm] UpdateUserDTO dto)
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
        [HttpGet("relationship-status")]
        public async Task<IActionResult> GetUserRelationshipStatus(
                  [FromQuery] int pageNumberBlocked = 1,
                  [FromQuery] int pageSizeBlocked = 10,
                  [FromQuery] int pageNumberRejected = 1,
                  [FromQuery] int pageSizeRejected = 10)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<UserRelationshipStatusDTO>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.GetUserRelationshipStatusAsync(
                userId,
                pageNumberBlocked,
                pageSizeBlocked,
                pageNumberRejected,
                pageSizeRejected
            );

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }

        [Authorize]
        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlockedUsers(
          [FromQuery] int pageNumber = 1,
          [FromQuery] int pageSize = 10) // Default values for pagination
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                // Ensure failure result type matches the expected success type for consistency
                return Unauthorized(Result<BlockedUserPagedDto>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.GetBlockedUsersAsync(userId, pageNumber, pageSize);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [Authorize]
        [HttpGet("blocked-by")]
        public async Task<IActionResult> GetBlockedByUsers(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10) // Default values for pagination
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<BlockingUserPagedDto>.Failure("User ID not found in token or invalid format."));
            }

            var result = await _userService.GetBlockedByUsersAsync(userId, pageNumber, pageSize);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpPost("block")] 
        [Authorize] 
        public async Task<ActionResult<Result<bool>>> BlockUser([FromBody] int blockedUserId)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<bool>.Failure("User ID not found in token or invalid."));
            }

            var result = await _userService.BlockUserAsync(userId, blockedUserId);

            if (!result.IsSuccess)
            {
                return BadRequest(result); 
            }
            return Ok(result); 
        }
        [HttpDelete("unblock/{unblockUserId}")] 
        [Authorize] 
        public async Task<ActionResult<Result<bool>>> UnblockUser(int unblockUserId)
        {
            var userIdClaim = User.FindFirst("id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(Result<bool>.Failure("User ID not found in token or invalid."));
            }

            var result = await _userService.UnblockUserAsync(userId, unblockUserId);

            if (!result.IsSuccess)
            {
                return BadRequest(result); 
            }
            return Ok(result);
        }
    }
}