using ChatApp.Models;
using ChatApp.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Hubs;

public class ChatHub(IChatMessageRepository repository) : Hub
{
    public async Task SendMessage(string user, string message)
    {
        await repository.SaveAsync(new ChatMessage
        {
            User = user,
            Text = message,
            SentAt = DateTime.UtcNow,
        });

        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }

    public override async Task OnConnectedAsync()
    {
        var history = await repository.GetRecentAsync(50);

        await Clients.Caller.SendAsync("MessageHistory", history);
        await base.OnConnectedAsync();
    }
}
