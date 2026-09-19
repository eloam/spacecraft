using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members.Responses;

/// <summary>
/// What somebody holding an invitation is shown before deciding.
/// </summary>
/// <remarks>
/// Everything here is already implied by the invitation itself: the email names the
/// organization and who sent it, and accepting would show the rest a moment later. What the
/// projects add is the answer to "is this the right Acme?", which is the question somebody
/// looking at a link from a stranger actually has.
/// </remarks>
/// <param name="OrganizationName">The organization's name.</param>
/// <param name="OrganizationSlug">Its address.</param>
/// <param name="Role">The role accepting would grant.</param>
/// <param name="InvitedBy">The name the inviter goes by, or the address they signed up with.</param>
/// <param name="ExpiresAt">When the invitation stops working.</param>
/// <param name="MemberCount">How many people are in the organization.</param>
/// <param name="Projects">The names of its newest projects, a few of them.</param>
/// <param name="AlreadyMember">Whether the signed-in account is already in this organization.</param>
public sealed record InvitationPreviewResponse(
    string OrganizationName,
    string OrganizationSlug,
    OrganizationRole Role,
    string InvitedBy,
    DateTimeOffset ExpiresAt,
    int MemberCount,
    List<string> Projects,
    bool AlreadyMember);
