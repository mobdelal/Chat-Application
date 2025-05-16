using Context;
using DTOs.Shared;
using DTOs.UserDTOs;
using Mappings.UserMapping;
using Microsoft.EntityFrameworkCore;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.UserSr
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        public UserService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Result<UserDTO>> RegisterAsync(RegisterDTO dto)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
                    return Result<UserDTO>.Failure("Username is already taken.");

                if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                    return Result<UserDTO>.Failure("Email is already in use.");

                string avatarUrl = string.Empty;
                if (dto.Avatar != null)
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
                    var mimeType = dto.Avatar.ContentType.ToLower();

                    if (!allowedMimeTypes.Contains(mimeType))
                    {
                        return Result<UserDTO>.Failure("Invalid file type. Please upload an image file (JPEG, PNG, GIF, BMP, WEBP, TIFF).");
                    }

                    var fileName = Guid.NewGuid() + "_" + dto.Avatar.FileName;
                    string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/avatars");

                    Directory.CreateDirectory(uploadFolder);

                    string filePath = Path.Combine(uploadFolder, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.Avatar.CopyToAsync(fileStream);
                    }

                    avatarUrl = "/images/avatars/" + fileName;
                }

                // Hash password
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                // Create user
                var user = new User
                {
                    Username = dto.Username,
                    Email = dto.Email,
                    AvatarUrl = avatarUrl,
                    IsOnline = true,
                    LastSeen = DateTime.UtcNow,
                    Password = hashedPassword
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return Result<UserDTO>.Success(user.ToUserDTO());
            }
            catch (Exception ex)
            {
                return Result<UserDTO>.Failure($"An error occurred during registration: {ex.Message}");
            }
        }
        public async Task<Result<UserDTO>> LoginAsync(LoginDTO dto)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == dto.UsernameOrEmail || u.Email == dto.UsernameOrEmail);

                if (user == null)
                    return Result<UserDTO>.Failure("Invalid username or email.");

                bool passwordMatches = BCrypt.Net.BCrypt.Verify(dto.Password, user.Password);
                if (!passwordMatches)
                    return Result<UserDTO>.Failure("Invalid password.");

                user.IsOnline = true;
                user.LastSeen = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Result<UserDTO>.Success(user.ToUserDTO());
            }
            catch (Exception ex)
            {
                return Result<UserDTO>.Failure($"An error occurred during login: {ex.Message}");
            }
        }
        public async Task<Result<UserDetailsDTO>> GetUserDetailsByIdAsync(int userId)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.BlockedUsers)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return Result<UserDetailsDTO>.Failure("User not found.");

                return Result<UserDetailsDTO>.Success(user.ToUserDetailsDTO());
            }
            catch (Exception ex)
            {
                return Result<UserDetailsDTO>.Failure($"An error occurred while retrieving user details: {ex.Message}");
            }
        }
        public async Task<Result<bool>> BlockUserAsync(int userId, int blockUserId)
        {
            try
            {
                if (userId == blockUserId)
                    return Result<bool>.Failure("You cannot block yourself.");

                var existingBlock = await _context.UserBlocks
                    .FirstOrDefaultAsync(b => b.BlockerId == userId && b.BlockedId == blockUserId);

                if (existingBlock != null)
                    return Result<bool>.Failure("User is already blocked.");

                var blocker = await _context.Users.FindAsync(userId);
                var blocked = await _context.Users.FindAsync(blockUserId);

                if (blocker == null || blocked == null)
                    return Result<bool>.Failure("User not found.");

                var userBlock = new UserBlock
                {
                    BlockerId = userId,
                    BlockedId = blockUserId,
                    BlockedAt = DateTime.UtcNow
                };

                _context.UserBlocks.Add(userBlock);
                await _context.SaveChangesAsync();

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while blocking the user: {ex.Message}");
            }
        }
        public async Task<Result<List<UserDTO>>> GetBlockedUsersAsync(int userId)
        {
            try
            {
                var blockedUsers = await _context.UserBlocks
                    .Where(b => b.BlockerId == userId)
                    .Select(b => b.Blocked)
                    .ToListAsync();

                var blockedUsersDto = blockedUsers.Select(u => u.ToUserDTO()).ToList();

                return Result<List<UserDTO>>.Success(blockedUsersDto);
            }
            catch (Exception ex)
            {
                return Result<List<UserDTO>>.Failure($"An error occurred while retrieving blocked users: {ex.Message}");
            }
        }
        public async Task<Result<UserDTO>> GetUserByUsernameAsync(string username)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return Result<UserDTO>.Failure("User not found.");

                return Result<UserDTO>.Success(user.ToUserDTO());
            }
            catch (Exception ex)
            {
                return Result<UserDTO>.Failure($"An error occurred while retrieving the user: {ex.Message}");
            }
        }
        public async Task<Result<bool>> IsUserBlockedAsync(int userId, int otherUserId)
        {
            try
            {
                bool isBlocked = await _context.UserBlocks
                    .AnyAsync(ub => ub.BlockerId == userId && ub.BlockedId == otherUserId);

                return Result<bool>.Success(isBlocked);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while checking block status: {ex.Message}");
            }
        }
        public async Task<Result<List<UserDTO>>> SearchUsersAsync(int searchingUserId, string searchTerm)
        {
            try
            {
                var blockedByUserIds = await _context.UserBlocks
                    .Where(b => b.BlockedId == searchingUserId)
                    .Select(b => b.BlockerId)
                    .ToListAsync();

                var users = await _context.Users
                    .Where(u => (u.Username.Contains(searchTerm) || u.Email.Contains(searchTerm)) &&
                                u.Id != searchingUserId &&
                                !blockedByUserIds.Contains(u.Id))
                    .ToListAsync();

                var usersDto = users.Select(u => u.ToUserDTO()).ToList();

                return Result<List<UserDTO>>.Success(usersDto);
            }
            catch (Exception ex)
            {
                return Result<List<UserDTO>>.Failure($"An error occurred while searching users: {ex.Message}");
            }
        }
        public async Task<Result<bool>> UnblockUserAsync(int userId, int unblockUserId)
        {
            try
            {
                var block = await _context.UserBlocks
                    .FirstOrDefaultAsync(b => b.BlockerId == userId && b.BlockedId == unblockUserId);

                if (block == null)
                    return Result<bool>.Failure("This user is not blocked.");

                _context.UserBlocks.Remove(block);
                await _context.SaveChangesAsync();

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"An error occurred while unblocking the user: {ex.Message}");
            }
        }
        public async Task<Result<List<UserDTO>>> GetContactsAsync(int userId)
        {
            try
            {
                // Get IDs of users the current user has blocked
                var blockedUserIds = await _context.UserBlocks
                    .Where(b => b.BlockerId == userId)
                    .Select(b => b.BlockedId)
                    .ToListAsync();

                // Get IDs of users who have blocked the current user
                var blockedByUserIds = await _context.UserBlocks
                    .Where(b => b.BlockedId == userId)
                    .Select(b => b.BlockerId)
                    .ToListAsync();

                var excludedUserIds = blockedUserIds.Concat(blockedByUserIds).ToHashSet();

                var contacts = await _context.Users
                    .Where(u => u.Id != userId && !excludedUserIds.Contains(u.Id))
                    .ToListAsync();

                var contactDtos = contacts.Select(u => u.ToUserDTO()).ToList();

                return Result<List<UserDTO>>.Success(contactDtos);
            }
            catch (Exception ex)
            {
                return Result<List<UserDTO>>.Failure($"An error occurred while retrieving contacts: {ex.Message}");
            }
        }


        public Task<Result<bool>> UpdateLastSeenAsync(int userId)
        {
            throw new NotImplementedException();
        }

        public Task<Result<bool>> UpdateUserStatusAsync(int userId, bool isOnline)
        {
            throw new NotImplementedException();
        }
    }
}
