using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IRoomRepository
{
    Task<Room> CreateAsync(string name, int creatorUserId);
    Task<Room?> GetByIdAsync(int id);
    Task<RoomMembership?> GetMembershipAsync(int roomId, int userId);
    Task<bool> IsMemberAsync(int roomId, int userId);
    Task<IReadOnlyList<RoomMembership>> GetMembersAsync(int roomId);
    Task<IReadOnlyList<RoomMembership>> GetMembershipsForUserAsync(int userId);
    Task AddMemberAsync(int roomId, int userId);
    Task RemoveMemberAsync(int roomId, int userId);
}
