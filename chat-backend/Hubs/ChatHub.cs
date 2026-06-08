using System.Security.Claims;
using ChatApp.Models;
using ChatApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Hubs;

[Authorize]
public class ChatHub(IChatMessageRepository repository) : Hub
{
    public async Task SendMessage(string message)
    {
        var username = Context.User!.FindFirstValue(ClaimTypes.Name)!;

        await repository.SaveAsync(new ChatMessage
        {
            User = username,
            Text = message,
            SentAt = DateTime.UtcNow,
        });

        await Clients.All.SendAsync("ReceiveMessage", username, message);
    }

    public override async Task OnConnectedAsync()
    {
        var history = await repository.GetRecentAsync(50);

        await Clients.Caller.SendAsync("MessageHistory", history);
        await base.OnConnectedAsync();
    }
}
