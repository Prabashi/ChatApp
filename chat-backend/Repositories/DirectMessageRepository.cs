using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class DirectMessageRepository(AppDbContext db) : IDirectMessageRepository
{
    public async Task SaveAsync(int conversationId, int userId, string text)
    {
        db.DirectMessages.Add(new DirectMessage
        {
            ConversationId = conversationId,
            UserId = userId,
            Text = text,
            SentAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<DirectMessage>> GetRecentAsync(int conversationId, int count = 50) =>
        await db.DirectMessages
            .Include(m => m.User)
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAt)
            .Take(count)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
}
