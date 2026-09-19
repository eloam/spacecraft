namespace SpaceCraft.Domain.Notifications;

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
