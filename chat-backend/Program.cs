using System.Text;
using ChatApp.Data;
using ChatApp.Hubs;
using ChatApp.Repositories;
using ChatApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=chat.db"));

builder.Services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Cookies.TryGetValue("auth_token", out var token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "ChatApp API is running.");
app.MapHub<ChatHub>("/chatHub");

app.MapPost("/auth/register", async (AuthRequest req, IAuthService auth, HttpResponse res) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Username and password are required." });

    var result = await auth.RegisterAsync(req.Username.Trim(), req.Password);
    if (!result.Success)
        return Results.Conflict(new { error = result.Error });

    SetAuthCookie(res, auth.GenerateToken(result.Username!));
    return Results.Ok(new { username = result.Username });
});

app.MapPost("/auth/login", async (AuthRequest req, IAuthService auth, HttpResponse res) =>
{
    var result = await auth.LoginAsync(req.Username.Trim(), req.Password);
    if (!result.Success)
        return Results.Unauthorized();

    SetAuthCookie(res, auth.GenerateToken(result.Username!));
    return Results.Ok(new { username = result.Username });
});

app.MapPost("/auth/logout", (HttpResponse res) =>
{
    res.Cookies.Delete("auth_token");
    return Results.Ok();
});

app.Run();

static void SetAuthCookie(HttpResponse res, string token) =>
    res.Cookies.Append("auth_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = false,
        Expires = DateTimeOffset.UtcNow.AddHours(24),
    });

record AuthRequest(string Username, string Password);
