using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class EmailTemplateTests
{
    private const string Link = "https://app.spacecraft.sh/reset-password?token=abc123";
    private const string Assets = "https://api.spacecraft.sh";

    [Fact]
    public void Every_message_carries_both_a_text_and_an_html_body()
    {
        // HTML alone reads as empty to a plain-text client and scores badly with spam
        // filters; text alone throws away the design.
        var messages = new[]
        {
            Emails.PasswordReset("you@example.com", Link, TimeSpan.FromHours(1)),
            Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "Yohann"),
            Emails.WaitlistConfirmation("you@example.com", "https://api.spacecraft.sh/confirm?token=x"),
        };

        foreach (var message in messages)
        {
            message.TextBody.ShouldNotBeNullOrWhiteSpace();
            message.HtmlBody.ShouldNotBeNullOrWhiteSpace();
            message.Subject.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_action_url_appears_in_both_bodies()
    {
        var message = Emails.PasswordReset("you@example.com", Link, TimeSpan.FromHours(1));

        // A reader on either side must be able to reach the link.
        message.TextBody.ShouldContain(Link);
        message.HtmlBody.ShouldNotBeNull().ShouldContain(Link);
    }

    [Fact]
    public void Html_uses_tables_and_inline_styles_rather_than_modern_layout()
    {
        var html = Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "Yohann").HtmlBody!;

        html.ShouldContain("<table");
        html.ShouldContain("style=\"");
        // Outlook renders neither, and a class-based stylesheet is stripped by Gmail.
        html.ShouldNotContain("display:flex");
        html.ShouldNotContain("display:grid");
    }

    [Fact]
    public void The_palette_matches_the_applications_own_tokens() =>
        // Converted from the oklch values in src/web/src/index.css; no mail client parses oklch.
        Emails.Welcome("you@example.com", "https://app.spacecraft.sh", null).HtmlBody!
            .ShouldContain("#171717");

    [Fact]
    public void A_missing_display_name_still_produces_a_sensible_greeting()
    {
        var message = Emails.Welcome("you@example.com", "https://app.spacecraft.sh", null);

        message.TextBody.ShouldNotContain("Welcome, ");
        message.TextBody.ShouldContain("account is ready");
    }

    [Fact]
    public void A_display_name_is_used_when_present() =>
        Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "Yohann")
            .TextBody.ShouldContain("Yohann");

    [Fact]
    public void Content_is_html_encoded()
    {
        // A display name is user-supplied and reaches the greeting; unencoded it would be
        // markup in someone else's inbox.
        var message = Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "<script>alert(1)</script>");

        message.HtmlBody.ShouldNotBeNull().ShouldNotContain("<script>");
        message.HtmlBody.ShouldContain("&lt;script&gt;");
    }

    [Theory]
    [InlineData(1, "an hour")]
    [InlineData(2, "2 hours")]
    public void Durations_read_as_prose_not_as_placeholders(int hours, string expected) =>
        // "expires in 1 hour(s)" is the sort of detail that makes software look unfinished.
        Emails.PasswordReset("you@example.com", Link, TimeSpan.FromHours(hours))
            .TextBody.ShouldContain(expected);

    [Fact]
    public void The_logo_is_an_absolute_url_with_an_empty_alt()
    {
        var html = Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "Yohann", Assets).HtmlBody!;

        // Absolute, because a mail client resolves it from the reader's machine with no
        // notion of the site it came from.
        html.ShouldContain("https://api.spacecraft.sh/email/logo.png");

        // alt="" on purpose: the wordmark sits beside it, and a described image would have
        // a blocked-image placeholder announce the brand twice.
        html.ShouldContain("alt=\"\"");
    }

    [Fact]
    public void Without_an_asset_base_the_header_is_still_valid()
    {
        // The wordmark carries the identity, so a message composed without assets — or read
        // with images blocked, which is the default in most clients — still looks deliberate.
        var html = Emails.Welcome("you@example.com", "https://app.spacecraft.sh", null).HtmlBody!;

        html.ShouldNotContain("<img");
        html.ShouldContain("SpaceCraft");
    }

    [Fact]
    public void A_preheader_is_present_so_inboxes_do_not_show_the_first_sentence()
    {
        var html = Emails.PasswordReset("you@example.com", Link, TimeSpan.FromHours(1)).HtmlBody!;

        html.ShouldContain("max-height:0");
    }

    [Fact]
    public void Rendering_writes_samples_for_visual_inspection()
    {
        // Not an assertion so much as a tap: the files are what a human looks at when the
        // design changes.
        var outDir = Path.Combine(Path.GetTempPath(), "email-preview");
        Directory.CreateDirectory(outDir);

        var samples = new (string Name, SpaceCraft.Api.Common.EmailMessage Message)[]
        {
            ("reset", Emails.PasswordReset("you@example.com", Link, TimeSpan.FromHours(1), Assets)),
            ("welcome", Emails.Welcome("you@example.com", "https://app.spacecraft.sh", "Yohann", Assets)),
            ("waitlist", Emails.WaitlistConfirmation("you@example.com", "https://api.spacecraft.sh/confirm?token=x", Assets)),
        };

        foreach (var (name, message) in samples)
        {
            File.WriteAllText(Path.Combine(outDir, $"{name}.html"), message.HtmlBody!);
        }

        File.Exists(Path.Combine(outDir, "reset.html")).ShouldBeTrue();
    }
}
