using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class RoomRepository(AppDbContext db) : IRoomRepository
{
    public async Task<Room> CreateAsync(string name, int creatorUserId)
    {
        var room = new Room { Name = name, CreatedAt = DateTime.UtcNow };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        db.RoomMemberships.Add(new RoomMembership
        {
            RoomId = room.Id,
            UserId = creatorUserId,
            Role = MemberRole.Admin,
            JoinedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return room;
    }

    public async Task<Room?> GetByIdAsync(int id) =>
        await db.Rooms.FindAsync(id);

    public async Task<RoomMembership?> GetMembershipAsync(int roomId, int userId) =>
        await db.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId);

    public async Task<bool> IsMemberAsync(int roomId, int userId) =>
        await db.RoomMemberships.AnyAsync(m => m.RoomId == roomId && m.UserId == userId);

    public async Task<IReadOnlyList<RoomMembership>> GetMembersAsync(int roomId) =>
        await db.RoomMemberships
            .Include(m => m.User)
            .Where(m => m.RoomId == roomId)
            .ToListAsync();

    public async Task<IReadOnlyList<RoomMembership>> GetMembershipsForUserAsync(int userId) =>
        await db.RoomMemberships
            .Include(m => m.Room)
            .Where(m => m.UserId == userId)
            .ToListAsync();

    public async Task AddMemberAsync(int roomId, int userId)
    {
        db.RoomMemberships.Add(new RoomMembership
        {
            RoomId = roomId,
            UserId = userId,
            Role = MemberRole.Member,
            JoinedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task RemoveMemberAsync(int roomId, int userId)
    {
        var membership = await db.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId);
        if (membership is not null)
        {
            db.RoomMemberships.Remove(membership);
            await db.SaveChangesAsync();
        }
    }
}
