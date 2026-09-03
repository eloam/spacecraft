using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// One thing that happened, addressed to one person.
/// </summary>
/// <remarks>
/// The row, with the <see cref="NotificationDelivery"/> rows beside it, is both the
/// notification somebody reads in the application and the outbox everything else is sent
/// from. That is deliberate: runs finish in the runner, a separate process with no mail
/// transport of its own, so the event has to survive a hop through the database either way.
/// Making that hop the notification as well means one description, every rendering — the same
/// principle <see cref="SpaceCraft.Api.Email.Emails"/> follows for its two bodies.
/// <para>
/// The wording is written here, at the moment the event happened, rather than rebuilt from
/// ids when someone opens the list. A run's target can be repointed and a member can leave;
/// what was true when the notification was raised is what it should still say.
/// </para>
/// </remarks>
public sealed class Notification : Entity
{
    private const int TitleLimit = 200;
    private const int SummaryLimit = 500;
    private const int DetailLimit = 2000;

    private readonly List<NotificationDelivery> _deliveries = [];

    private Notification() { }

    public Notification(
        Guid userId,
        Guid organizationId,
        NotificationKind kind,
        string title,
        string summary,
        string? detail,
        string link,
        string actionLabel,
        bool showInApp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionLabel);

        UserId = userId;
        OrganizationId = organizationId;
        Kind = kind;
        Title = Text.Clamp(title, TitleLimit)!;
        Summary = Text.Clamp(summary, SummaryLimit)!;
        Detail = detail is null ? null : Text.Clamp(detail, DetailLimit);
        Link = link;
        ActionLabel = actionLabel;
        ShowInApp = showInApp;
    }

    /// <summary>Who it is for. Always a member of <see cref="OrganizationId"/> when written.</summary>
    public Guid UserId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; } = null!;

    /// <summary>One sentence. The line under the title in the list, and the first paragraph of the email.</summary>
    public string Summary { get; private set; } = null!;

    /// <summary>The particulars, where there are any: a failure reason, a published version.</summary>
    public string? Detail { get; private set; }

    /// <summary>
    /// Where it points, as a path inside the application.
    /// </summary>
    /// <remarks>
    /// Built from ids rather than slugs. A slug moves when an organization is renamed, and a
    /// link written months ago has to keep working; the routes accept either form.
    /// </remarks>
    public string Link { get; private set; } = null!;

    public string ActionLabel { get; private set; } = null!;

    /// <summary>
    /// Whether it belongs in the reader's list, or exists only to carry an outbound message.
    /// </summary>
    /// <remarks>
    /// A flag rather than a delivery of its own: the in-app list has nothing to push anywhere,
    /// so it cannot fail and has nothing to retry. Keeping it here also keeps the list — the
    /// query that runs whenever anybody opens the bell — reading one table.
    /// </remarks>
    public bool ShowInApp { get; private set; }

    /// <summary>The outbound channels this was raised for, each with its own state.</summary>
    public IReadOnlyCollection<NotificationDelivery> Deliveries => _deliveries;

    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>
    /// Asks for this to go out on a channel. Nothing sends it here — a dispatcher sweeps the
    /// deliveries and does that, which is what lets the runner raise a notification from a
    /// process with no transport of its own.
    /// </summary>
    public void RequestDelivery(NotificationChannel channel)
    {
        if (_deliveries.Any(d => d.Channel == channel))
        {
            return;
        }

        _deliveries.Add(new NotificationDelivery(Id, channel));
    }

    public void MarkRead(DateTimeOffset at)
    {
        ReadAt ??= at;
        Touch();
    }
}
