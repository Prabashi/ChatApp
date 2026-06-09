using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public async Task<Conversation> GetOrCreateAsync(int userIdA, int userIdB)
    {
        var existing = await db.Conversations
            .Where(c =>
                c.Participants.Any(p => p.UserId == userIdA) &&
                c.Participants.Any(p => p.UserId == userIdB))
            .FirstOrDefaultAsync();

        if (existing is not null) return existing;

        var conversation = new Conversation { CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        db.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userIdA,
        });
        db.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userIdB,
        });
        await db.SaveChangesAsync();

        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(int userId) =>
        await db.Conversations
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .ToListAsync();

    public async Task<bool> IsParticipantAsync(int conversationId, int userId) =>
        await db.ConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId);
}
