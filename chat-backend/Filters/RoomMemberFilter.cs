using System.Security.Claims;
using ChatApp.Repositories;

namespace ChatApp.Filters;

public class RoomMemberFilter(IRoomRepository rooms) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var userIdStr = ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roomIdStr = ctx.HttpContext.GetRouteValue("id")?.ToString();

        if (!int.TryParse(userIdStr, out var userId) || !int.TryParse(roomIdStr, out var roomId))
            return Results.BadRequest();

        if (!await rooms.IsMemberAsync(roomId, userId))
            return Results.Forbid();

        return await next(ctx);
    }
}
