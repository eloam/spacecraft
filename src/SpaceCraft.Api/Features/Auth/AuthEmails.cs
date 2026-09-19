using SpaceCraft.Api.Email;

namespace SpaceCraft.Api.Features.Auth;

/// <summary>
/// What signing up and resetting a password put in someone's inbox.
/// </summary>
public static class AuthEmails
{
    public static EmailMessage PasswordReset(string to, string link, TimeSpan lifetime, string? assetBaseUrl = null)
    {
        var (text, html) = EmailTemplate.Compose(
            heading: "Reset your password",
            preheader: "Choose a new password — this link works once.",
            paragraphs:
            [
                "Someone asked to reset the password for this account. Choose a new one using "
                + "the link below.",
            ],
            action: ("Choose a new password", link),
            footnote: $"The link works once and expires in {EmailTemplate.Duration(lifetime)}. "
                + "If this wasn't you, ignore this email — your password has not changed.",
            assetBaseUrl: assetBaseUrl);

        return new EmailMessage(to, "Reset your SpaceCraft password", text, html);
    }

    public static EmailMessage Welcome(string to, string appUrl, string? displayName, string? assetBaseUrl = null)
    {
        // The name is optional at registration, so the greeting has to work without it.
        var greeting = string.IsNullOrWhiteSpace(displayName)
            ? "Your SpaceCraft account is ready."
            : $"Welcome, {displayName.Trim()} — your SpaceCraft account is ready.";

        var (text, html) = EmailTemplate.Compose(
            heading: "Welcome to SpaceCraft",
            preheader: "Your account is ready — here's how to publish your first SDK.",
            paragraphs:
            [
                greeting,
                "Create an organization, upload an OpenAPI document, and SpaceCraft validates "
                + "it, generates a typed SDK and publishes it to your registry — with the "
                + "version tracking the one in your spec.",
            ],
            action: ("Open SpaceCraft", appUrl),
            footnote: "You're receiving this because an account was created with this address. "
                + "If it wasn't you, reply and let us know.",
            assetBaseUrl: assetBaseUrl);

        return new EmailMessage(to, "Welcome to SpaceCraft", text, html);
    }
}
