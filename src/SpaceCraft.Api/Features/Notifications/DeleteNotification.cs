using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>
/// Throws one away.
/// </summary>
/// <remarks>
/// A real delete, and its deliveries go with it. If the email for this notification has
/// not been swept yet — at most a few seconds after it was raised — dismissing it stops
/// that too, which is the reading of "delete" somebody dismissing a notice expects. Once
/// the message has left, nothing here can call it back.
/// </remarks>
public static class DeleteNotification
{
    public static async Task<IResult> HandleAsync(
        Guid notificationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();

        // Scoped to the reader, not merely to the id: one person's list is not another's to
        // tidy up.
        var removed = await db.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return removed == 0 ? ApiResults.NotFound("The notification") : TypedResults.NoContent();
    }
}
