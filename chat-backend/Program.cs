using System.Security.Claims;
using System.Text;
using ChatApp.Data;
using ChatApp.Filters;
using ChatApp.Hubs;
using ChatApp.Repositories;
using ChatApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=chat.db"));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IRoomMessageRepository, RoomMessageRepository>();
builder.Services.AddScoped<IDirectMessageRepository, DirectMessageRepository>();

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

// ── Auth ──────────────────────────────────────────────────────────────────────

app.MapGet("/auth/me", (ClaimsPrincipal user) =>
    Results.Ok(new { username = user.FindFirstValue(ClaimTypes.Name) })
).RequireAuthorization();

app.MapPost("/auth/register", async (AuthRequest req, IAuthService auth, HttpResponse res) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Username and password are required." });

    var result = await auth.RegisterAsync(req.Username.Trim(), req.Password);
    if (!result.Success)
        return Results.Conflict(new { error = result.Error });

    SetAuthCookie(res, auth.GenerateToken(result.User!));
    return Results.Ok(new { username = result.User!.Username });
});

app.MapPost("/auth/login", async (AuthRequest req, IAuthService auth, HttpResponse res) =>
{
    var result = await auth.LoginAsync(req.Username.Trim(), req.Password);
    if (!result.Success)
        return Results.Unauthorized();

    SetAuthCookie(res, auth.GenerateToken(result.User!));
    return Results.Ok(new { username = result.User!.Username });
});

app.MapPost("/auth/logout", (HttpResponse res) =>
{
    res.Cookies.Delete("auth_token");
    return Results.Ok();
});

// ── Rooms ─────────────────────────────────────────────────────────────────────

app.MapPost("/rooms", async (CreateRoomRequest req, ClaimsPrincipal user, IRoomRepository rooms) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = "Room name is required." });

    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var room = await rooms.CreateAsync(req.Name.Trim(), userId);
    return Results.Ok(new { room.Id, room.Name });
}).RequireAuthorization();

app.MapGet("/rooms", async (ClaimsPrincipal user, IRoomRepository rooms) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var memberships = await rooms.GetMembershipsForUserAsync(userId);
    return Results.Ok(memberships.Select(m => new
    {
        id = m.RoomId,
        name = m.Room.Name,
        isAdmin = m.Role == ChatApp.Models.MemberRole.Admin,
    }));
}).RequireAuthorization();

app.MapGet("/rooms/{id}/members", async (int id, IRoomRepository rooms) =>
{
    var members = await rooms.GetMembersAsync(id);
    return Results.Ok(members.Select(m => new
    {
        userId = m.UserId,
        username = m.User.Username,
        role = m.Role.ToString().ToLower(),
    }));
}).RequireAuthorization().AddEndpointFilter<RoomMemberFilter>();

app.MapPost("/rooms/{id}/members", async (
    int id,
    AddMemberRequest req,
    ClaimsPrincipal user,
    IRoomRepository rooms,
    IUserRepository users,
    IHubContext<ChatHub> hub) =>
{
    var targetUser = await users.FindByUsernameAsync(req.Username);
    if (targetUser is null) return Results.NotFound(new { error = "User not found." });

    if (await rooms.IsMemberAsync(id, targetUser.Id))
        return Results.Conflict(new { error = "User is already a member." });

    await rooms.AddMemberAsync(id, targetUser.Id);

    var room = await rooms.GetByIdAsync(id);
    await hub.Clients.User(targetUser.Id.ToString())
        .SendAsync("RoomAdded", new { id, name = room!.Name, isAdmin = false });

    return Results.Ok();
}).RequireAuthorization().AddEndpointFilter<RoomAdminFilter>();

app.MapDelete("/rooms/{id}/members/{userId}", async (
    int id,
    int userId,
    ClaimsPrincipal user,
    IRoomRepository rooms,
    IHubContext<ChatHub> hub) =>
{
    var currentUserId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    if (userId == currentUserId)
        return Results.BadRequest(new { error = "Admin cannot remove themselves." });

    await rooms.RemoveMemberAsync(id, userId);
    await hub.Clients.User(userId.ToString()).SendAsync("RoomRemoved", new { id });

    return Results.Ok();
}).RequireAuthorization().AddEndpointFilter<RoomAdminFilter>();

// ── Conversations ─────────────────────────────────────────────────────────────

app.MapPost("/conversations", async (
    StartConversationRequest req,
    ClaimsPrincipal user,
    IUserRepository users,
    IConversationRepository conversations,
    IHubContext<ChatHub> hub) =>
{
    var currentUserId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var currentUsername = user.FindFirstValue(ClaimTypes.Name)!;

    var targetUser = await users.FindByUsernameAsync(req.Username);
    if (targetUser is null) return Results.NotFound(new { error = "User not found." });
    if (targetUser.Id == currentUserId)
        return Results.BadRequest(new { error = "Cannot start a conversation with yourself." });

    var conversation = await conversations.GetOrCreateAsync(currentUserId, targetUser.Id);

    await hub.Clients.User(targetUser.Id.ToString())
        .SendAsync("ConversationStarted", new { id = conversation.Id, fromUsername = currentUsername });

    return Results.Ok(new { id = conversation.Id, otherUsername = targetUser.Username });
}).RequireAuthorization();

app.MapGet("/conversations", async (ClaimsPrincipal user, IConversationRepository conversations) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var convos = await conversations.GetConversationsForUserAsync(userId);
    return Results.Ok(convos.Select(c => new
    {
        id = c.Id,
        otherUsername = c.Participants.FirstOrDefault(p => p.UserId != userId)?.User.Username ?? "",
    }));
}).RequireAuthorization();

// ── Users ─────────────────────────────────────────────────────────────────────

app.MapGet("/users", async (string? search, IUserRepository users) =>
{
    if (string.IsNullOrWhiteSpace(search)) return Results.Ok(Array.Empty<object>());
    var results = await users.SearchByUsernameAsync(search.Trim());
    return Results.Ok(results.Select(u => new { u.Id, u.Username }));
}).RequireAuthorization();

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
record CreateRoomRequest(string Name);
record AddMemberRequest(string Username);
record StartConversationRequest(string Username);
