using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IConversationRepository
{
    Task<Conversation> GetOrCreateAsync(int userIdA, int userIdB);
    Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(int userId);
    Task<bool> IsParticipantAsync(int conversationId, int userId);
}
