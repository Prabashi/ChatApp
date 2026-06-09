using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatApp.Models;
using ChatApp.Repositories;
using Microsoft.IdentityModel.Tokens;

namespace ChatApp.Services;

public class AuthService(IUserRepository users, IConfiguration config) : IAuthService
{
    public async Task<AuthResult> RegisterAsync(string username, string password)
    {
        if (await users.ExistsAsync(username))
            return new AuthResult(false, null, "Username is already taken.");

        var user = await users.CreateAsync(new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
        });

        return new AuthResult(true, user, null);
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var user = await users.FindByUsernameAsync(username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return new AuthResult(false, null, "Invalid username or password.");

        return new AuthResult(true, user, null);
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
            ],
            expires: DateTime.UtcNow.AddHours(
                double.Parse(config["Jwt:ExpiryHours"]!)),
            signingCredentials: new SigningCredentials(
                key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
