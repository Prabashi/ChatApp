using System.Security.Claims;
using ChatApp.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Hubs;

[Authorize]
public class ChatHub(
    IRoomRepository rooms,
    IConversationRepository conversations,
    IRoomMessageRepository roomMessages,
    IDirectMessageRepository directMessages) : Hub
{
    private int UserId => int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Username => Context.User!.FindFirstValue(ClaimTypes.Name)!;

    public async Task SendMessage(int targetId, string targetType, string message)
    {
        var userId = UserId;
        var username = Username;
        var sentAt = DateTime.UtcNow;

        if (targetType == "room")
        {
            if (!await rooms.IsMemberAsync(targetId, userId)) return;
            await roomMessages.SaveAsync(targetId, userId, message);
            await Clients.Group($"room:{targetId}").SendAsync("ReceiveMessage", new
            {
                targetId,
                targetType = "room",
                user = username,
                text = message,
                sentAt,
            });
        }
        else if (targetType == "conversation")
        {
            if (!await conversations.IsParticipantAsync(targetId, userId)) return;
            await directMessages.SaveAsync(targetId, userId, message);
            await Clients.Group($"dm:{targetId}").SendAsync("ReceiveMessage", new
            {
                targetId,
                targetType = "conversation",
                user = username,
                text = message,
                sentAt,
            });
        }
    }

    public async Task GetHistory(int targetId, string targetType)
    {
        var userId = UserId;

        if (targetType == "room")
        {
            if (!await rooms.IsMemberAsync(targetId, userId)) return;
            var history = await roomMessages.GetRecentAsync(targetId);
            await Clients.Caller.SendAsync("MessageHistory", new
            {
                targetId,
                targetType = "room",
                messages = history.Select(m => new
                {
                    id = m.Id,
                    user = m.User.Username,
                    text = m.Text,
                    sentAt = m.SentAt,
                }),
            });
        }
        else if (targetType == "conversation")
        {
            if (!await conversations.IsParticipantAsync(targetId, userId)) return;
            var history = await directMessages.GetRecentAsync(targetId);
            await Clients.Caller.SendAsync("MessageHistory", new
            {
                targetId,
                targetType = "conversation",
                messages = history.Select(m => new
                {
                    id = m.Id,
                    user = m.User.Username,
                    text = m.Text,
                    sentAt = m.SentAt,
                }),
            });
        }
    }

    public async Task JoinRoom(int roomId)
    {
        if (!await rooms.IsMemberAsync(roomId, UserId)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room:{roomId}");
    }

    public async Task JoinConversation(int conversationId)
    {
        if (!await conversations.IsParticipantAsync(conversationId, UserId)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"dm:{conversationId}");
    }

    public async Task LeaveRoom(int roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room:{roomId}");
    }

    public override async Task OnConnectedAsync()
    {
        var userId = UserId;

        var memberships = await rooms.GetMembershipsForUserAsync(userId);
        foreach (var m in memberships)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room:{m.RoomId}");

        var userConversations = await conversations.GetConversationsForUserAsync(userId);
        foreach (var c in userConversations)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"dm:{c.Id}");

        await Clients.Caller.SendAsync("RoomList", memberships.Select(m => new
        {
            id = m.RoomId,
            name = m.Room.Name,
            isAdmin = m.Role == Models.MemberRole.Admin,
        }));

        await Clients.Caller.SendAsync("ConversationList", userConversations.Select(c => new
        {
            id = c.Id,
            otherUsername = c.Participants.FirstOrDefault(p => p.UserId != userId)?.User.Username ?? "",
        }));

        await base.OnConnectedAsync();
    }
}
