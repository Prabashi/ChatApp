using ChatApp.Models;

namespace ChatApp.Services;

public record AuthResult(bool Success, User? User, string? Error);

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string username, string password);
    Task<AuthResult> LoginAsync(string username, string password);
    string GenerateToken(User user);
}
