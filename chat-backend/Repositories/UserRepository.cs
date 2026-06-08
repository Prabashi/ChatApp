using ChatApp.Data;
using ChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Repositories;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> FindByUsernameAsync(string username) =>
        await db.Users.FirstOrDefaultAsync(u => u.Username == username);

    public async Task<bool> ExistsAsync(string username) =>
        await db.Users.AnyAsync(u => u.Username == username);

    public async Task<User> CreateAsync(User user)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
