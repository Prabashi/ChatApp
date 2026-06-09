using System.Security.Claims;
using ChatApp.Models;
using ChatApp.Repositories;

namespace ChatApp.Filters;

public class RoomAdminFilter(IRoomRepository rooms) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var userIdStr = ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roomIdStr = ctx.HttpContext.GetRouteValue("id")?.ToString();

        if (!int.TryParse(userIdStr, out var userId) || !int.TryParse(roomIdStr, out var roomId))
            return Results.BadRequest();

        var membership = await rooms.GetMembershipAsync(roomId, userId);
        if (membership is null || membership.Role != MemberRole.Admin)
            return Results.Forbid();

        return await next(ctx);
    }
}
