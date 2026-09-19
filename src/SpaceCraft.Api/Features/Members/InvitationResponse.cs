using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members;

/// <summary>What an outstanding invitation looks like to the admin who sent it.</summary>
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    OrganizationRole Role,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt)
{
    public static InvitationResponse From(OrganizationInvitation invitation) =>
        new(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, invitation.CreatedAt);
}
