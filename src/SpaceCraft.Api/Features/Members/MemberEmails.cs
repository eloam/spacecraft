using SpaceCraft.Api.Email;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members;

/// <summary>
/// The invitation to join an organization.
/// </summary>
public static class MemberEmails
{
    /// <summary>
    /// The invitation to join an organization. The link carries the whole grant.
    /// </summary>
    /// <remarks>
    /// Addressed to a mailbox rather than to an account, deliberately: this message is what
    /// proves the address belongs to whoever accepts. Membership was previously handed to
    /// whatever account already used the address, which nothing had ever verified.
    /// </remarks>
    public static EmailMessage OrganizationInvitation(
        string to,
        string organizationName,
        string invitedBy,
        string role,
        string acceptUrl,
        TimeSpan lifetime,
        string? assetBaseUrl = null)
    {
        var days = Math.Max(1, (int)Math.Round(lifetime.TotalDays));

        var (text, html) = EmailTemplate.Compose(
            heading: $"Join {organizationName} on SpaceCraft",
            preheader: $"{invitedBy} invited you to {organizationName}.",
            paragraphs:
            [
                $"{invitedBy} invited you to join {organizationName} on SpaceCraft as {role}.",
                "SpaceCraft turns OpenAPI documents into SDKs and publishes them to package "
                + "registries. Accepting adds this address's account to the organization.",
                $"The link works once, and for {days} day{(days == 1 ? "" : "s")}.",
            ],
            action: ("Accept the invitation", acceptUrl),
            footnote: "If you weren't expecting this, ignore it. Nothing happens until the "
                + "link is opened, and it expires on its own.",
            assetBaseUrl: assetBaseUrl);

        return new EmailMessage(to, $"You're invited to {organizationName} on SpaceCraft", text, html);
    }
}
