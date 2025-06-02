using Context;
using DTOs.Shared;
using DTOs.UserDTOs;
using Mappings.UserMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Models;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using DTOs.ChatDTOs;
using Mappings.ChatMappings;


namespace Application.UserSr
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        private readonly JwtSettings _jwtSettings;
        private readonly string _webRootPath;

        public UserService(AppDbContext context, IOptions<JwtSettings> jwtSettings)
        {
            _context = context;
            _jwtSettings = jwtSettings.Value;
            _webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        public async Task<Result<UserDTO>> RegisterAsync(RegisterDTO dto)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
                    return Result<UserDTO>.Failure("Username is already taken.");

                if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                    return Result<UserDTO>.Failure("Email is already in use.");

                string avatarUrl = "/images/default/defaultUser.png";

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
                    // Use _webRootPath for consistency and correct base path
                    string uploadFolder = Path.Combine(_webRootPath, "images", "avatars");

                    Directory.CreateDirectory(uploadFolder);

                    string filePath = Path.Combine(uploadFolder, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.Avatar.CopyToAsync(fileStream);
                    }

                    avatarUrl = "/images/avatars/" + fileName;
                }

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

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

        public async Task<Result<string>> LoginAsync(LoginDTO dto)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == dto.UsernameOrEmail || u.Email == dto.UsernameOrEmail);

                if (user == null)
                    return Result<string>.Failure("Invalid username or email.");

                bool passwordMatches = BCrypt.Net.BCrypt.Verify(dto.Password, user.Password);
                if (!passwordMatches)
                    return Result<string>.Failure("Invalid password.");

                user.IsOnline = true;
                user.LastSeen = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var token = GenerateJwtToken(user);

                return Result<string>.Success(token);
            }
            catch (Exception ex)
            {
                return Result<string>.Failure($"An error occurred during login: {ex.Message}");
            }
        }

        private string GenerateJwtToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username!),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim("id", user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username!)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
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

        public async Task<Result<BlockedUserPagedDto>> GetBlockedUsersAsync(int userId, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                pageNumber = Math.Max(1, pageNumber);
                pageSize = Math.Max(1, pageSize);

                var query = _context.UserBlocks.Where(ub => ub.BlockerId == userId);
                var totalCount = await query.CountAsync();

                var singleBlockedUsers = await query
                    .OrderByDescending(ub => ub.BlockedAt) // Order for consistent pagination
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(ub => new SingleBlockedUserDto // Select into the single user DTO
                    {
                        UserId = ub.BlockedId,
                        Username = ub.Blocked.Username,
                        AvatarUrl = ub.Blocked.AvatarUrl,
                        BlockedAt = ub.BlockedAt
                    })
                    .ToListAsync();

                var pagedDto = new BlockedUserPagedDto
                {
                    Items = singleBlockedUsers,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                return Result<BlockedUserPagedDto>.Success(pagedDto);
            }
            catch (Exception ex)
            {
                return Result<BlockedUserPagedDto>.Failure($"An error occurred while fetching blocked users: {ex.Message}");
            }
        }

        public async Task<Result<BlockingUserPagedDto>> GetBlockedByUsersAsync(int userId, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                pageNumber = Math.Max(1, pageNumber);
                pageSize = Math.Max(1, pageSize);

                var query = _context.UserBlocks.Where(ub => ub.BlockedId == userId);
                var totalCount = await query.CountAsync();

                var singleBlockingUsers = await query
                    .OrderByDescending(ub => ub.BlockedAt) // Order for consistent pagination
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(ub => new SingleBlockingUserDto // Select into the single user DTO
                    {
                        UserId = ub.BlockerId,
                        Username = ub.Blocker.Username,
                        AvatarUrl = ub.Blocker.AvatarUrl,
                        BlockedAt = ub.BlockedAt
                    })
                    .ToListAsync();

                var pagedDto = new BlockingUserPagedDto
                {
                    Items = singleBlockingUsers,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                return Result<BlockingUserPagedDto>.Success(pagedDto);
            }
            catch (Exception ex)
            {
                return Result<BlockingUserPagedDto>.Failure($"An error occurred while fetching users who blocked you: {ex.Message}");
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

        public async Task<Result<List<UserDTO>>> SearchUsersAsync(int searchingUserId, string searchTerm, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return Result<List<UserDTO>>.Success(new List<UserDTO>());
                }

                var upperSearchTerm = searchTerm.ToUpper();

                // IDs of users who have blocked the current searchingUserId
                var blockedByUserIds = await _context.UserBlocks
                    .Where(b => b.BlockedId == searchingUserId)
                    .Select(b => b.BlockerId)
                    .ToListAsync();

                // IDs of users that the current searchingUserId has blocked
                var usersBlockedByCurrentUserIds = await _context.UserBlocks
                    .Where(b => b.BlockerId == searchingUserId)
                    .Select(b => b.BlockedId)
                    .ToListAsync();

                var query = _context.Users
                    .Where(u => (u.Username.ToUpper().Contains(upperSearchTerm) ||
                                  u.Email.ToUpper().Contains(upperSearchTerm)) &&
                                 u.Id != searchingUserId &&
                                 !blockedByUserIds.Contains(u.Id) &&
                                 !usersBlockedByCurrentUserIds.Contains(u.Id));

                var users = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var usersDto = users.Select(u => u.ToUserDTO()).ToList();

                return Result<List<UserDTO>>.Success(usersDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SearchUsersAsync: {ex.Message} - {ex.StackTrace}");
                return Result<List<UserDTO>>.Failure($"An error occurred while searching users. Please try again later.");
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

        public async Task<Result<bool>> UnblockUserAsync(int userId, int unblockUserId)
        {
            try
            {
                var block = await _context.UserBlocks
                    .FirstOrDefaultAsync(b => b.BlockerId == userId && b.BlockedId == unblockUserId);

                if (block == null)
                {
                    return Result<bool>.Failure("This user is not blocked.");
                }

                _context.UserBlocks.Remove(block);
                await _context.SaveChangesAsync();
                var rejectedChat = await _context.Chats
                    .Include(c => c.Participants)
                    .Where(c => !c.IsGroup && c.Status == ChatStatus.Rejected)
                    .Where(c => c.Participants.Any(p => p.UserId == userId) &&
                                c.Participants.Any(p => p.UserId == unblockUserId))
                    .FirstOrDefaultAsync();

                if (rejectedChat != null)
                {

                    rejectedChat.Status = ChatStatus.Active;
                    await _context.SaveChangesAsync();

                }

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

        public Result<int> GetUserIdFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Result<int>.Failure("Token cannot be null or empty.");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null)
                {
                    return Result<int>.Failure("Invalid JWT token format.");
                }

                var userIdClaim = principal.Claims.FirstOrDefault(c => c.Type == "id");
                if (userIdClaim == null)
                {
                    return Result<int>.Failure("User ID claim ('id') not found in token.");
                }

                if (!int.TryParse(userIdClaim.Value, out int userId))
                {
                    return Result<int>.Failure("Invalid user ID format in token.");
                }

                return Result<int>.Success(userId);
            }
            catch (SecurityTokenExpiredException)
            {
                return Result<int>.Failure("Token has expired.");
            }
            catch (SecurityTokenValidationException ex)
            {
                Console.WriteLine($"SecurityTokenValidationException in GetUserIdFromToken: {ex.Message}");
                return Result<int>.Failure($"Invalid token: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetUserIdFromToken: {ex}");
                return Result<int>.Failure($"An error occurred while processing the token: {ex.Message}");
            }
        }

        public async Task<Result<bool>> UpdateUserStatusAsync(int userId, bool isOnline)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return Result<bool>.Failure("User not found.");
                }

                user.IsOnline = isOnline;

                await _context.SaveChangesAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"Failed to update online status: {ex.Message}");
            }
        }

        public async Task<Result<bool>> UpdateLastSeenAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return Result<bool>.Failure("User not found.");
                }

                user.LastSeen = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure($"Failed to update last seen: {ex.Message}");
            }
        }

        public async Task<Result<bool>> ChangeUserPasswordAsync(int userId, ChangePasswordDTO dto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Result<bool>.Failure("User not found.");
                }

                if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.Password))
                {
                    return Result<bool>.Failure("Incorrect current password.");
                }

                user.Password = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                await _context.SaveChangesAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error changing password for user {userId}: {ex}");
                return Result<bool>.Failure($"An error occurred while changing password: {ex.Message}");
            }
        }

        public async Task<Result<bool>> UpdateUserProfileAsync(int userId, UpdateUserDTO dto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Result<bool>.Failure("User not found.");
                }

                if (dto.Username != null)
                {
                    if (await _context.Users.AnyAsync(u => u.Username == dto.Username && u.Id != userId))
                    {
                        return Result<bool>.Failure("Username is already taken.");
                    }
                    user.Username = dto.Username;
                }

                if (dto.AvatarFile != null)
                {
                    var allowedMimeTypes = new[]
                    {
                        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "image/tiff"
                    };
                    var mimeType = dto.AvatarFile.ContentType.ToLower();

                    if (!allowedMimeTypes.Contains(mimeType))
                    {
                        return Result<bool>.Failure("Invalid file type. Please upload an image file (JPEG, PNG, GIF, BMP, WEBP, TIFF).");
                    }

                    string uploadsFolder = Path.Combine(_webRootPath, "images", "avatars");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    string defaultAvatarPath = "/images/default/defaultUser.png";
                    if (!string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl != defaultAvatarPath)
                    {
                        string oldAvatarFullPath = Path.Combine(_webRootPath, user.AvatarUrl.TrimStart('/'));
                        if (File.Exists(oldAvatarFullPath))
                        {
                            try
                            {
                                File.Delete(oldAvatarFullPath);
                                Console.WriteLine($"Deleted old avatar: {oldAvatarFullPath}");
                            }
                            catch (IOException ioEx)
                            {
                                Console.WriteLine($"Error deleting old avatar {oldAvatarFullPath}: {ioEx.Message}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Unexpected error deleting old avatar {oldAvatarFullPath}: {ex.Message}");
                            }
                        }
                    }

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(dto.AvatarFile.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.AvatarFile.CopyToAsync(fileStream);
                    }

                    user.AvatarUrl = "/images/avatars/" + uniqueFileName;
                }

                if (dto.ReceiveNotifications.HasValue)
                {
                    user.ReceiveNotifications = dto.ReceiveNotifications.Value;
                }

                await _context.SaveChangesAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating profile for user {userId}: {ex}");
                return Result<bool>.Failure($"An error occurred while updating profile: {ex.Message}");
            }
        }

        public async Task<Result<UserRelationshipStatusDTO>> GetUserRelationshipStatusAsync(
            int userId,
            int pageNumberBlocked = 1,
            int pageSizeBlocked = 10,
            int pageNumberRejected = 1,
            int pageSizeRejected = 10)
        {
            try
            {
                // Validate pagination parameters
                if (pageNumberBlocked < 1) pageNumberBlocked = 1;
                if (pageSizeBlocked < 1 || pageSizeBlocked > 50) pageSizeBlocked = 10; // Adjust max page size as needed
                if (pageNumberRejected < 1) pageNumberRejected = 1;
                if (pageSizeRejected < 1 || pageSizeRejected > 50) pageSizeRejected = 10; // Adjust max page size as needed

                // --- Blocked Users (Already correctly fetches users 'userId' is blocking) ---
                var blockedUsersQuery = _context.UserBlocks
                    .AsNoTracking()
                    .Where(ub => ub.BlockerId == userId);

                // Get total count BEFORE applying Skip/Take for pagination metadata
                var totalBlockedUsers = await blockedUsersQuery.CountAsync();

                var blockedUserIds = await blockedUsersQuery
                    .Select(ub => ub.BlockedId)
                    .Skip((pageNumberBlocked - 1) * pageSizeBlocked) // Apply pagination
                    .Take(pageSizeBlocked)                             // Apply pagination
                    .ToListAsync();

                var blockedUsersDTOs = new List<UserDTO>();
                if (blockedUserIds.Any())
                {
                    blockedUsersDTOs = await _context.Users
                        .AsNoTracking()
                        .Where(u => blockedUserIds.Contains(u.Id))
                        // Assuming you have a ToUserDTO extension or similar for User to UserDTO mapping
                        .Select(u => u.ToUserDTO())
                        .ToListAsync();
                }

                // --- Rejected Chats ---
                var rejectedChatsQuery = _context.Chats
                    .AsNoTracking()
                    .Where(c => c.Participants.Any(p => p.UserId == userId && c.Status == ChatStatus.Rejected))
                    .Where(c => !c.Participants.Any(p => p.UserId == userId && p.IsAdmin)); // Filter for non-admin rejected chats

                // Get total count BEFORE applying Skip/Take
                var totalRejectedChats = await rejectedChatsQuery.CountAsync();

                var rejectedChatsPaged = await rejectedChatsQuery
                    .OrderByDescending(c => c.CreatedAt) // Order by creation date or last activity for consistency
                    .Skip((pageNumberRejected - 1) * pageSizeRejected) // Apply pagination
                    .Take(pageSizeRejected)                             // Apply pagination
                    .Include(c => c.Participants)
                        .ThenInclude(p => p.User)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Sender)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Attachments)
                    .Include(c => c.Messages.Where(m => !m.IsDeleted))
                        .ThenInclude(m => m.Reactions)
                    .ToListAsync();

                var rejectedChatDTOs = new List<ChatDTO>();
                foreach (var chat in rejectedChatsPaged) // Iterate through the Paged results
                {
                    // --- ADIT: Pass the blockedUserIds list to the ToDTO extension method ---
                    rejectedChatDTOs.Add(chat.ToDTO(userId, blockedUserIds));
                }

                var resultDTO = new UserRelationshipStatusDTO
                {
                    BlockedUsers = blockedUsersDTOs,
                    TotalBlockedUsers = totalBlockedUsers, // Set total count
                    RejectedChats = rejectedChatDTOs,
                    TotalRejectedChats = totalRejectedChats // Set total count
                };

                return Result<UserRelationshipStatusDTO>.Success(resultDTO);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetUserRelationshipStatusAsync: {ex}");
                return Result<UserRelationshipStatusDTO>.Failure($"An error occurred while fetching relationship status: {ex.Message}");
            }
        }
    }
}