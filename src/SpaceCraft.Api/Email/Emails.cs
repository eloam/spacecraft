using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Email;

/// <summary>
/// The messages the application sends. Kept together so their tone and structure stay
/// consistent, and so both bodies of each are built from one description.
/// </summary>
public static class Emails
{
    /// <summary>
    /// "an hour", "45 minutes" — not "1 hour(s)". A parenthesised plural in a message a
    /// customer reads is the kind of detail that makes software look unfinished.
    /// </summary>
    private static string Duration(TimeSpan value) => value switch
    {
        { TotalHours: 1 } => "an hour",
        { TotalHours: >= 1 } => $"{value.TotalHours:0} hours",
        { TotalMinutes: 1 } => "a minute",
        _ => $"{value.TotalMinutes:0} minutes",
    };

    public static EmailMessage PasswordReset(string to, string link, TimeSpan lifetime)
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
            footnote: $"The link works once and expires in {Duration(lifetime)}. "
                + "If this wasn't you, ignore this email — your password has not changed.");

        return new EmailMessage(to, "Reset your SpaceCraft password", text, html);
    }

    public static EmailMessage Welcome(string to, string appUrl, string? displayName)
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
                + "If it wasn't you, reply and let us know.");

        return new EmailMessage(to, "Welcome to SpaceCraft", text, html);
    }

    public static EmailMessage WaitlistConfirmation(string to, string confirmUrl)
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
                + "and we won't email you again.");

        return new EmailMessage(to, "Confirm your SpaceCraft waitlist signup", text, html);
    }
}
