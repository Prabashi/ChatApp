using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class RoomMessageRepository(AppDbContext db) : IRoomMessageRepository
{
    public async Task SaveAsync(int roomId, int userId, string text)
    {
        db.RoomMessages.Add(new RoomMessage
        {
            RoomId = roomId,
            UserId = userId,
            Text = text,
            SentAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<RoomMessage>> GetRecentAsync(int roomId, int count = 50) =>
        await db.RoomMessages
            .Include(m => m.User)
            .Where(m => m.RoomId == roomId)
            .OrderByDescending(m => m.SentAt)
            .Take(count)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
}
