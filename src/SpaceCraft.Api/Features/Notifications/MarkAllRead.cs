using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Notifications;

public static class MarkAllRead
{
    public static async Task<NoContent> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();
        var now = DateTimeOffset.UtcNow;

        // Everything unread, not only what the list shows. The button says all, and clearing
        // more than is visible is the harmless direction: nothing hidden becomes unread again
        // if the caller rejoins an organization they had left.
        await db.Notifications
            .Where(n => n.UserId == userId && n.ShowInApp && n.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.ReadAt, now)
                    .SetProperty(n => n.UpdatedAt, now),
                ct);

        return TypedResults.NoContent();
    }
}
