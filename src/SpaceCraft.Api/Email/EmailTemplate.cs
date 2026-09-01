using System.Net;
using System.Text;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Renders transactional emails in the application's own visual language.
/// <para>
/// Mail clients are not browsers. Layout is tables rather than flex or grid, every style is
/// inline because Gmail strips much of a &lt;style&gt; block, and the palette is literal hex
/// because no client understands the <c>oklch()</c> the app's CSS is written in. The values
/// below are those tokens converted: the neutral greys of <c>src/web/src/index.css</c>.
/// </para>
/// </summary>
public static class EmailTemplate
{
    // The application's own light theme, from src/web/src/index.css, converted from oklch —
    // which no mail client understands — to hex. The token each one carries is named beside
    // it, because these are copies: retuning the palette there leaves these behind, and the
    // last time it was retuned it did. The greys here were neutral while the application's had
    // gone mauve, so an email sat next to the product looked like somebody else's.
    private const string Background = "#ffffff";   // --card
    private const string Surface = "#f3f1f3";      // --page, the ground the whole app sits on
    private const string Foreground = "#0c090c";   // --foreground
    private const string Primary = "#1d161e";      // --primary
    private const string PrimaryForeground = "#fafafa"; // --primary-foreground
    private const string Muted = "#79697b";        // --muted-foreground
    private const string Border = "#e7e4e7";       // --border

    private const string FontStack =
        "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    /// <summary>
    /// Wraps content in the shared frame.
    /// </summary>
    /// <param name="preheader">
    /// The snippet shown next to the subject in an inbox list. Left unset, clients grab the
    /// first words of the body, which is usually the greeting and tells the reader nothing.
    /// </param>
    public static string Render(string heading, string preheader, string bodyHtml, string? assetBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);

        // Most clients block remote images until the reader allows them, so the mark is
        // decoration and the wordmark beside it carries the identity. An alt of "" keeps a
        // blocked image from announcing itself twice next to the text that follows.
        var logo = assetBaseUrl is { Length: > 0 }
            ? $"""<img src="{Escape(assetBaseUrl.TrimEnd('/'))}/email/logo.png" width="28" height="28" alt="" style="display:block;border:0;" />"""
            : string.Empty;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{Escape(heading)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:{Surface};">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{Escape(preheader)}</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                     style="background-color:{Surface};padding:40px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                           style="max-width:520px;">
                      <tr>
                        <td align="center" style="padding-bottom:28px;">
                          <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                            <tr>
                              <td style="padding-right:8px;">{logo}</td>
                              <td style="font-family:{FontStack};font-size:17px;font-weight:600;
                                         letter-spacing:-0.01em;color:{Foreground};">SpaceCraft</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="background-color:{Background};border:1px solid {Border};
                                   border-radius:12px;padding:36px 32px;">
                          <h1 style="margin:0 0 18px;font-family:{FontStack};font-size:21px;
                                     font-weight:600;letter-spacing:-0.02em;color:{Foreground};">
                            {Escape(heading)}
                          </h1>
                          {bodyHtml}
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-top:26px;font-family:{FontStack};
                                   font-size:12px;line-height:19px;color:{Muted};">
                          SpaceCraft — SDKs from your OpenAPI documents
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    public static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;font-family:{FontStack};font-size:15px;line-height:24px;color:{Foreground};">{Escape(text)}</p>""";

    /// <summary>
    /// A link in the small print, for the one thing a footnote is worth acting on: where to
    /// change what arrives. Muted like the text around it — it is an offer, not a call.
    /// </summary>
    public static string SmallPrintLink(string label, string url) =>
        $"""<p style="margin:4px 0 0;font-family:{FontStack};font-size:13px;line-height:21px;"><a href="{Escape(url)}" style="color:{Muted};">{Escape(label)}</a></p>""";

    public static string SmallPrint(string text) =>
        $"""<p style="margin:16px 0 0;font-family:{FontStack};font-size:13px;line-height:21px;color:{Muted};">{Escape(text)}</p>""";

    /// <summary>
    /// A table-based button. An anchor styled as a block is ignored by Outlook, which renders
    /// its own box model; wrapping it in a table with a background colour is what survives.
    /// </summary>
    public static string Button(string label, string url) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:24px 0 8px;">
          <tr>
            <td align="center" bgcolor="{Primary}" style="border-radius:8px;">
              <a href="{Escape(url)}" style="display:inline-block;padding:12px 22px;font-family:{FontStack};
                        font-size:14px;font-weight:500;color:{PrimaryForeground};text-decoration:none;
                        border-radius:8px;">{Escape(label)}</a>
            </td>
          </tr>
        </table>
        """;

    /// <summary>
    /// The destination in full, under the button. Some clients strip buttons, some readers
    /// distrust them, and a long URL wrapped in a mail client is unusable without this.
    /// </summary>
    public static string UrlFallback(string url) =>
        $"""
        <p style="margin:8px 0 0;font-family:{FontStack};font-size:12px;line-height:19px;color:{Muted};
                  word-break:break-all;">
          Or paste this into your browser:<br>
          <span style="color:{Muted};">{Escape(url)}</span>
        </p>
        """;

    public static string Divider() =>
        $"""<div style="height:1px;background-color:{Border};margin:26px 0 0;"></div>""";

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Builds both bodies at once, so they cannot drift apart.</summary>
    public static (string Text, string Html) Compose(
        string heading, string preheader, string[] paragraphs, (string Label, string Url)? action = null,
        string? footnote = null, string? assetBaseUrl = null, (string Label, string Url)? footnoteLink = null)
    {
        var html = new StringBuilder();
        var text = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            html.Append(Paragraph(paragraph));
            text.AppendLine(paragraph).AppendLine();
        }

        if (action is { } cta)
        {
            html.Append(Button(cta.Label, cta.Url)).Append(UrlFallback(cta.Url));
            text.AppendLine(cta.Url).AppendLine();
        }

        if (footnote is not null)
        {
            html.Append(Divider()).Append(SmallPrint(footnote));
            text.AppendLine(footnote);

            if (footnoteLink is { } link)
            {
                html.Append(SmallPrintLink(link.Label, link.Url));
                // The bare address, because the text part has no anchors and "click here" in
                // a plain-text body is a dead end.
                text.AppendLine(link.Url);
            }
        }

        return (text.ToString().TrimEnd(), Render(heading, preheader, html.ToString(), assetBaseUrl));
    }
}
