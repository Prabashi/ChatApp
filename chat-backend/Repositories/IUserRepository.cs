using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username);
    Task<User?> FindByIdAsync(int id);
    Task<bool> ExistsAsync(string username);
    Task<User> CreateAsync(User user);
    Task<IReadOnlyList<User>> SearchByUsernameAsync(string prefix, int limit = 10);
}
