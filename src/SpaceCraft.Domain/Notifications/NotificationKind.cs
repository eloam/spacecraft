namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// The things worth telling somebody about.
/// </summary>
/// <remarks>
/// Persisted by value, so the numbers are part of the schema: add at the end, never renumber.
/// </remarks>
public enum NotificationKind
{
    /// <summary>Someone accepted an invitation and is now a member.</summary>
    MemberJoined = 1,

    /// <summary>Someone left the organization, or was removed from it.</summary>
    MemberLeft = 2,

    /// <summary>A run reached a result — published, or failed with a reason.</summary>
    RunCompleted = 3,

    /// <summary>
    /// You were removed from an organization. Addressed to the person it happened to, unlike
    /// <see cref="MemberLeft"/>, which tells the people who answer for the organization.
    /// </summary>
    RemovedFromOrganization = 4,

    /// <summary>Somebody turned an invitation down. The answer the sender was waiting for.</summary>
    InvitationDeclined = 5,
}
