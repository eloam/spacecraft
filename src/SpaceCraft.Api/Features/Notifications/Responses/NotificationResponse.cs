namespace SpaceCraft.Api.Features.Notifications.Responses;

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
