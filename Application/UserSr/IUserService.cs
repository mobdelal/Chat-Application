using DTOs.Shared;
using DTOs.UserDTOs;

namespace Application.UserSr
{
    public interface IUserService
    {
        Task<Result<UserDTO>> RegisterAsync(RegisterDTO dto);
        Task<Result<UserDTO>> LoginAsync(LoginDTO dto);
        Task<Result<UserDetailsDTO>> GetUserDetailsByIdAsync(int userId);
        Task<Result<UserDTO>> GetUserByUsernameAsync(string username);
        Task<Result<List<UserDTO>>> SearchUsersAsync(int searchingUserId, string searchTerm);
        Task<Result<List<UserDTO>>> GetContactsAsync(int userId);
        Task<Result<bool>> BlockUserAsync(int userId, int blockUserId);
        Task<Result<bool>> UnblockUserAsync(int userId, int unblockUserId);
        Task<Result<List<UserDTO>>> GetBlockedUsersAsync(int userId);
        Task<Result<bool>> IsUserBlockedAsync(int userId, int otherUserId);
        Task<Result<bool>> UpdateUserStatusAsync(int userId, bool isOnline);
        Task<Result<bool>> UpdateLastSeenAsync(int userId);
    }
}
