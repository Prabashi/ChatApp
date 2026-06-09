using System.Security.Claims;
using ChatApp.Repositories;

namespace ChatApp.Filters;

public class ConversationParticipantFilter(IConversationRepository conversations) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var userIdStr = ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var convIdStr = ctx.HttpContext.GetRouteValue("id")?.ToString();

        if (!int.TryParse(userIdStr, out var userId) || !int.TryParse(convIdStr, out var convId))
            return Results.BadRequest();

        if (!await conversations.IsParticipantAsync(convId, userId))
            return Results.Forbid();

        return await next(ctx);
    }
}
