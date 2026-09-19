using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>
/// Empties the list, sparing what has not been read unless told otherwise.
/// </summary>
/// <remarks>
/// The default is the careful one. Tidying up is the common intention and losing something
/// unread is the expensive mistake, so unread notifications survive a clear that does not
/// mention them — and the interface asks before it does.
/// <para>
/// Only what the list holds: rows that exist only to carry an outbound message were never
/// shown here and are left to the dispatcher.
/// </para>
/// </remarks>
public static class ClearNotifications
{
    public static async Task<NoContent> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct,
        [FromQuery] bool includeUnread = false)
    {
        var userId = principal.Id();

        await db.Notifications
            .Where(n => n.UserId == userId && n.ShowInApp && (includeUnread || n.ReadAt != null))
            .ExecuteDeleteAsync(ct);

        return TypedResults.NoContent();
    }
}
