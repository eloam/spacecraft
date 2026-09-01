using Microsoft.EntityFrameworkCore;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Infrastructure.Notifications;

/// <summary>
/// What a notification says, before it is addressed to anybody.
/// </summary>
/// <param name="Link">A path inside the application, built from ids rather than slugs.</param>
public sealed record NotificationContent(
    NotificationKind Kind,
    string Title,
    string Summary,
    string? Detail,
    string Link,
    string ActionLabel);

/// <summary>
/// Raises notifications, deciding for each recipient which channels they asked for.
/// </summary>
/// <remarks>
/// Lives here rather than in the API because the runner raises them too: a run finishes in a
/// process with no mail transport of its own, and the row it writes is what the API's
/// dispatcher later sends.
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>
    /// Tells the people who answer for the organization — its owners and admins — leaving out
    /// whoever caused it.
    /// </summary>
    /// <remarks>
    /// The actor is excluded because being told what you have just done is noise, and noise
    /// is what makes people turn a feature like this off wholesale.
    /// </remarks>
    Task PublishToGovernanceAsync(
        Guid organizationId, Guid? actor, NotificationContent content, CancellationToken ct);

    /// <summary>
    /// Tells one person, if they are still a member. Somebody who has left an organization
    /// stops hearing about it, including about work they started.
    /// </summary>
    Task PublishToMemberAsync(
        Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct);

    /// <summary>
    /// Tells one person whether or not they are a member.
    /// </summary>
    /// <remarks>
    /// For the one message that exists because they are not: being removed from an
    /// organization is news you cannot be told by anything that first checks you belong to it.
    /// </remarks>
    Task PublishToPersonAsync(
        Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct);
}

public sealed class NotificationPublisher(SpaceCraftDbContext db) : INotificationPublisher
{
    public async Task PublishToGovernanceAsync(
        Guid organizationId, Guid? actor, NotificationContent content, CancellationToken ct)
    {
        var recipients = await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId
                        && m.Role >= OrganizationRole.Admin
                        && (actor == null || m.UserId != actor))
            .Select(m => m.UserId)
            .ToListAsync(ct);

        await PublishAsync(organizationId, recipients, content, ct);
    }

    public async Task PublishToMemberAsync(
        Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct)
    {
        var isMember = await db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);

        await PublishAsync(organizationId, isMember ? [userId] : [], content, ct);
    }

    public Task PublishToPersonAsync(
        Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct) =>
        PublishAsync(organizationId, [userId], content, ct);

    private async Task PublishAsync(
        Guid organizationId,
        List<Guid> recipients,
        NotificationContent content,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (recipients.Count == 0)
        {
            return;
        }

        // Only the rows that differ from the defaults exist, so this is a small read even for
        // an organization that has been running for years.
        var stated = await db.NotificationPreferences
            .AsNoTracking()
            .Where(p => recipients.Contains(p.UserId) && p.Kind == content.Kind)
            .ToListAsync(ct);

        foreach (var recipient in recipients)
        {
            // Nothing here names a channel. A new one in the enum starts being honoured the
            // day it is added, for anybody who has not said otherwise.
            var wanted = NotificationDefaults.Channels
                .Where(channel => Wants(stated, recipient, content.Kind, channel))
                .ToList();

            if (wanted.Count == 0)
            {
                // Said no to all of them. Writing the row anyway would leave it in a list
                // nobody reads and an outbox nobody sends from.
                continue;
            }

            var notification = new Notification(
                recipient,
                organizationId,
                content.Kind,
                content.Title,
                content.Summary,
                content.Detail,
                content.Link,
                content.ActionLabel,
                showInApp: wanted.Contains(NotificationChannel.InApp));

            foreach (var channel in wanted.Where(c => c != NotificationChannel.InApp))
            {
                notification.RequestDelivery(channel);
            }

            db.Notifications.Add(notification);
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool Wants(
        List<NotificationPreference> stated,
        Guid userId,
        NotificationKind kind,
        NotificationChannel channel) =>
        stated.FirstOrDefault(p => p.UserId == userId && p.Channel == channel) is { } preference
            ? preference.Enabled
            : NotificationDefaults.IsEnabled(kind, channel);
}
