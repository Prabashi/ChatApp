namespace ChatApp.Services;

public record AuthResult(bool Success, string? Username, string? Error);

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string username, string password);
    Task<AuthResult> LoginAsync(string username, string password);
    string GenerateToken(string username);
}
