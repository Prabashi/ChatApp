using ChatApp.Models;

namespace ChatApp.Repositories;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username);
    Task<bool> ExistsAsync(string username);
    Task<User> CreateAsync(User user);
}
