using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class ChatMessageRepository(AppDbContext db) : IChatMessageRepository
{
    public async Task SaveAsync(ChatMessage message)
    {
        db.Messages.Add(message);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int count = 50)
    {
        return await db.Messages
            .OrderByDescending(m => m.SentAt)
            .Take(count)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }
}
