using SpaceCraft.Api.Email;

namespace SpaceCraft.Api.Features.Waitlist;

/// <summary>
/// The one message the waitlist sends: confirm the address before it is kept.
/// </summary>
public static class WaitlistEmails
{
    public static EmailMessage WaitlistConfirmation(string to, string confirmUrl, string? assetBaseUrl = null)
    {
        var (text, html) = EmailTemplate.Compose(
            heading: "Confirm your email",
            preheader: "One click to join the SpaceCraft waitlist.",
            paragraphs:
            [
                "Thanks for your interest in SpaceCraft. Confirm this address to join the "
                + "waitlist and hear from us at launch.",
            ],
            action: ("Confirm my email", confirmUrl),
            footnote: "If you didn't request this, ignore this message — no account is created "
                + "and we won't email you again.",
            assetBaseUrl: assetBaseUrl);

        return new EmailMessage(to, "Confirm your SpaceCraft waitlist signup", text, html);
    }
}
