using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>One entry in the reader's list.</summary>
/// <remarks>
/// <paramref name="Kind"/> is a name rather than a number. Roles travel as numbers because
/// they are ordered and compared; these are not, and a settings screen that has to hold a
/// table mapping 3 to "a run finished" is a screen that will disagree with the server the
/// first time the enum grows.
/// </remarks>
public sealed record NotificationResponse(
    Guid Id,
    string Kind,
    Guid OrganizationId,
    string OrganizationName,
    string Title,
    string Summary,
    string? Detail,
    string Link,
    string ActionLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationFeedResponse(int UnreadCount, List<NotificationResponse> Items);

/// <summary>One switch on the settings page: this kind, on this channel.</summary>
public sealed record NotificationPreferenceState(string Kind, string Channel, bool Enabled);

public sealed record NotificationPreferencesRequest(List<NotificationPreferenceState> Preferences);

public static class NotificationEndpoints
{
    /// <summary>How many the list returns at once. The bell is a recent history, not an archive.</summary>
    private const int DefaultLimit = 30;
    private const int MaxLimit = 100;

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var group = api.MapGroup("/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<NotificationFeedResponse>()
            .WithSummary("The caller's notifications, newest first.");

        group.MapPost("/read-all", MarkAllReadAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Marks every unread notification as read.");

        group.MapPost("/{notificationId:guid}/read", MarkReadAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Marks one notification as read.");

        group.MapDelete("/{notificationId:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Removes one notification from the caller's list.");

        group.MapDelete("/", ClearAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Removes the caller's read notifications, and the unread ones on request.");

        group.MapGet("/preferences", GetPreferencesAsync)
            .Produces<List<NotificationPreferenceState>>()
            .WithSummary("What the caller has chosen to receive, and on which channels.");

        group.MapPut("/preferences", SavePreferencesAsync)
            .Produces<List<NotificationPreferenceState>>()
            .ProducesValidationProblem()
            .WithSummary("Replaces the caller's notification settings.");

        return api;
    }

    private static async Task<IResult> ListAsync(
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

    private static async Task<IResult> MarkReadAsync(
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

    private static async Task<IResult> MarkAllReadAsync(
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

    /// <summary>
    /// Throws one away.
    /// </summary>
    /// <remarks>
    /// A real delete, and its deliveries go with it. If the email for this notification has
    /// not been swept yet — at most a few seconds after it was raised — dismissing it stops
    /// that too, which is the reading of "delete" somebody dismissing a notice expects. Once
    /// the message has left, nothing here can call it back.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
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
    private static async Task<IResult> ClearAsync(
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

    private static async Task<IResult> GetPreferencesAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var stated = await LoadAsync(db, principal.Id(), ct);
        return TypedResults.Ok(Matrix(stated));
    }

    /// <summary>
    /// Replaces the whole matrix.
    /// </summary>
    /// <remarks>
    /// The page sends every switch it shows, so a partial save cannot leave the settings
    /// half-applied — and anything the request does not mention keeps whatever it had, which
    /// is what lets a future channel ship without every account having to re-save.
    /// </remarks>
    private static async Task<IResult> SavePreferencesAsync(
        [FromBody] NotificationPreferencesRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Keyed rather than listed, so a payload naming the same switch twice settles on its
        // last word instead of trying to write two rows the unique index will not take — which
        // reached the caller as a 500 on a request that was merely repetitive.
        var parsed = new Dictionary<(NotificationKind Kind, NotificationChannel Channel), bool>();

        foreach (var state in request.Preferences ?? [])
        {
            if (!Enum.TryParse<NotificationKind>(state.Kind, ignoreCase: true, out var kind)
                || !NotificationDefaults.Kinds.Contains(kind))
            {
                return ApiResults.Invalid(nameof(state.Kind), $"'{state.Kind}' is not a notification we send.");
            }

            if (!Enum.TryParse<NotificationChannel>(state.Channel, ignoreCase: true, out var channel)
                || !NotificationDefaults.Channels.Contains(channel))
            {
                return ApiResults.Invalid(nameof(state.Channel), $"'{state.Channel}' is not a channel we deliver on.");
            }

            parsed[(kind, channel)] = state.Enabled;
        }

        var userId = principal.Id();
        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(ct);

        foreach (var ((kind, channel), enabled) in parsed)
        {
            var row = existing.FirstOrDefault(p => p.Kind == kind && p.Channel == channel);

            if (row is null)
            {
                db.NotificationPreferences.Add(new NotificationPreference(userId, kind, channel, enabled));
            }
            else
            {
                row.Set(enabled);
            }
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(Matrix(await LoadAsync(db, userId, ct)));
    }

    private static async Task<List<NotificationPreference>> LoadAsync(
        SpaceCraftDbContext db, Guid userId, CancellationToken ct) =>
        await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);

    /// <summary>
    /// Every switch the settings page shows, with the defaults filled in where nothing has
    /// been said. The page never has to know what the defaults are.
    /// </summary>
    private static List<NotificationPreferenceState> Matrix(List<NotificationPreference> stated) =>
    [
        .. from kind in NotificationDefaults.Kinds
           from channel in NotificationDefaults.Channels
           select new NotificationPreferenceState(
               kind.ToString(),
               channel.ToString(),
               NotificationAddressing.IsEnabled(stated, kind, channel))
    ];
}
