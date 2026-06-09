using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IRoomMessageRepository
{
    Task SaveAsync(int roomId, int userId, string text);
    Task<IReadOnlyList<RoomMessage>> GetRecentAsync(int roomId, int count = 50);
}
