namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// What a notification says, before it is addressed to anybody.
/// </summary>
/// <param name="Kind">What happened, which decides who is told and on which channels.</param>
/// <param name="Title">The one line the reader sees first.</param>
/// <param name="Summary">What happened, in a sentence.</param>
/// <param name="Detail">A further sentence when there is one to give.</param>
/// <param name="Link">A path inside the application, built from ids rather than slugs.</param>
/// <param name="ActionLabel">What the link is called.</param>
public sealed record NotificationContent(
    NotificationKind Kind,
    string Title,
    string Summary,
    string? Detail,
    string Link,
    string ActionLabel);
