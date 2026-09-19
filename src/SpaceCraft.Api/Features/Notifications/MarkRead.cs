using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Notifications;

public static class MarkRead
{
    public static async Task<IResult> HandleAsync(
        Guid notificationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();

        // Scoped to the reader, not merely to the id: marking somebody else's notification
        // read would hide it from them.
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

        if (notification is null)
        {
            return ApiResults.NotFound("The notification");
        }

        notification.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
