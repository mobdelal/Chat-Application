﻿using DTOs.Shared;
using DTOs.UserDTOs;

namespace Application.UserSr
{
    public interface IUserService
    {
        Task<Result<UserDTO>> RegisterAsync(RegisterDTO dto);
        Task<Result<string>> LoginAsync(LoginDTO dto);
        Task<Result<UserDetailsDTO>> GetUserDetailsByIdAsync(int userId);
        Task<Result<UserDTO>> GetUserByUsernameAsync(string username);
        Task<Result<List<UserDTO>>> SearchUsersAsync(int searchingUserId, string searchTerm, int pageNumber = 1, int pageSize = 20);
        Task<Result<List<UserDTO>>> GetContactsAsync(int userId);
        Task<Result<bool>> BlockUserAsync(int userId, int blockUserId);
        Task<Result<bool>> UnblockUserAsync(int userId, int unblockUserId);
        Task<Result<BlockedUserPagedDto>> GetBlockedUsersAsync(int userId, int pageNumber = 1, int pageSize = 10);
        Task<Result<BlockingUserPagedDto>> GetBlockedByUsersAsync(int userId, int pageNumber = 1, int pageSize = 10);
        Task<Result<bool>> IsUserBlockedAsync(int userId, int otherUserId);
        Task<Result<bool>> UpdateUserStatusAsync(int userId, bool isOnline);
        Task<Result<bool>> UpdateLastSeenAsync(int userId);
        Result<int> GetUserIdFromToken(string token);
        Task<Result<bool>> ChangeUserPasswordAsync(int userId, ChangePasswordDTO dto);
        Task<Result<bool>> UpdateUserProfileAsync(int userId, UpdateUserDTO dto);
        Task<Result<UserRelationshipStatusDTO>> GetUserRelationshipStatusAsync(
            int userId,
            int pageNumberBlocked = 1, 
            int pageSizeBlocked = 10,  
            int pageNumberRejected = 1, 
            int pageSizeRejected = 10 
        );

    }
}
