using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IDirectMessageRepository
{
    Task SaveAsync(int conversationId, int userId, string text);
    Task<IReadOnlyList<DirectMessage>> GetRecentAsync(int conversationId, int count = 50);
}
