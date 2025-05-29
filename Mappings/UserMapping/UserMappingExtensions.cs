using DTOs.UserDTOs;
using Models;
namespace Mappings.UserMapping
{
    public static class UserMappingExtensions
    {
        public static UserDTO ToUserDTO(this User user)
        {
            if (user == null) return null!;

            return new UserDTO
            {
                Id = user.Id,
                Username = user.Username,
                AvatarUrl = user.AvatarUrl,
                IsOnline = user.IsOnline,
                LastSeen = user.LastSeen
            };
        }
        public static UserDetailsDTO ToUserDetailsDTO(this User user)
        {
            if (user == null) return null!;

            return new UserDetailsDTO
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                IsOnline = user.IsOnline,
                LastSeen = user.LastSeen,
                ReceiveNotifications = user.ReceiveNotifications,
                BlockedUsersIds = user.BlockedUsers?.Select(b => b.BlockedId).ToList() ?? new List<int>()
            };
        }

    }
}
