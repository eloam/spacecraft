using SpaceCraft.Api.Email;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>
/// A notification, as it reads when it leaves by mail instead of staying in the inbox.
/// </summary>
public static class NotificationEmails
{
    /// <summary>
    /// A notification, sent by mail.
    /// </summary>
    /// <remarks>
    /// One composer for every kind rather than one per event, because the wording was already
    /// decided when the notification was raised — by whoever knew what had happened. This only
    /// dresses it: the same title, summary and detail the reader sees in the application,
    /// wrapped in the frame the other messages use, so the two never drift apart.
    /// </remarks>
    public static EmailMessage Notification(
        string to,
        string organizationName,
        string title,
        string summary,
        string? detail,
        string actionLabel,
        string actionUrl,
        string settingsUrl,
        string? assetBaseUrl = null)
    {
        string[] paragraphs = detail is null ? [summary] : [summary, detail];

        var (text, html) = EmailTemplate.Compose(
            heading: title,
            preheader: summary,
            paragraphs: paragraphs,
            action: (actionLabel, actionUrl),
            footnote: $"You're receiving this because of your notification settings for "
                + $"{organizationName}.",
            assetBaseUrl: assetBaseUrl,
            footnoteLink: ("Change what SpaceCraft emails you", settingsUrl));

        // The only messages carrying List-Unsubscribe: somebody chose to receive these and
        // can stop them. A password reset or an invitation is an answer to a request, and
        // offering to unsubscribe from one would be an offer we could not keep.
        return new EmailMessage(to, title, text, html, UnsubscribeUrl: settingsUrl);
    }
}
