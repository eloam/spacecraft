using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Notifications.Responses;

namespace SpaceCraft.Api.Features.Notifications;

public static class ListNotifications
{
    /// <summary>How many the list returns at once. The bell is a recent history, not an archive.</summary>
    private const int DefaultLimit = 30;

    private const int MaxLimit = 100;

    public static async Task<Ok<NotificationFeedResponse>> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct,
        [FromQuery] int? limit = null,
        [FromQuery] bool unreadOnly = false)
    {
        var userId = principal.Id();
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        // Rows that exist only to carry an email are not part of anybody's list — and neither
        // are organizations the caller has left. Leaving one ends what it can tell you: every
        // item it left behind points at a page that would now answer 404, and the titles name
        // the people and packages of a team you are no longer on.
        var mine = db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId
                        && n.ShowInApp
                        && (db.OrganizationMembers.Any(
                                m => m.OrganizationId == n.OrganizationId && m.UserId == userId)
                            // The one exception, and the reason for it is in its name: being
                            // removed from an organization is precisely the news that the
                            // membership test above would throw away.
                            || n.Kind == NotificationKind.RemovedFromOrganization));

        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);

        var rows = await (
            from notification in unreadOnly ? mine.Where(n => n.ReadAt == null) : mine
            join organization in db.Organizations.AsNoTracking()
                on notification.OrganizationId equals organization.Id
            orderby notification.CreatedAt descending
            select new
            {
                notification.Id,
                notification.Kind,
                notification.OrganizationId,
                organization.Name,
                organization.Slug,
                notification.Title,
                notification.Summary,
                notification.Detail,
                notification.Link,
                notification.ActionLabel,
                notification.CreatedAt,
                notification.ReadAt,
            })
            .Take(take)
            .ToListAsync(ct);

        var items = rows
            .Select(row => new NotificationResponse(
                row.Id,
                row.Kind.ToString(),
                row.OrganizationId,
                row.Name,
                row.Title,
                row.Summary,
                row.Detail,
                // Kept with ids so that a rename cannot break a record written months ago,
                // handed over with today's slug so the address bar reads like every other
                // link in the application rather than showing a guid where a name belongs.
                row.Link.Replace(
                    $"/orgs/{row.OrganizationId}", $"/orgs/{row.Slug}", StringComparison.Ordinal),
                row.ActionLabel,
                row.CreatedAt,
                row.ReadAt))
            .ToList();

        return TypedResults.Ok(new NotificationFeedResponse(unread, items));
    }
}
