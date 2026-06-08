using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IChatMessageRepository
{
    Task SaveAsync(ChatMessage message);
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int count = 50);
}
