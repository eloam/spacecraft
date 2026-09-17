using System.ComponentModel.DataAnnotations;
using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SmtpEmailOptionsTests
{
    private static SmtpEmailOptions Configured(
        int port = 587, SmtpSecurity security = SmtpSecurity.StartTls) => new()
    {
        Host = "smtp.tem.scaleway.com",
        Port = port,
        Security = security,
        Username = "a-project-id",
        Password = "secret",
        FromEmail = "noreply@example.test",
    };

    /// <summary>
    /// A relay to reach, a name to give it and a password to say it with. All three are
    /// written down: nothing about them is derived from a particular provider, which is what
    /// lets the relay be swapped without touching code.
    /// </summary>
    [Fact]
    public void A_host_a_username_and_a_password_are_what_make_the_transport_usable() =>
        Configured().IsConfigured.ShouldBeTrue();

    [Theory]
    [InlineData("Host")]
    [InlineData("Username")]
    [InlineData("Password")]
    public void Half_a_configuration_does_not_count(string missing)
    {
        // Silently sending nothing is the failure this prevents: an incomplete relay selects
        // the logging transport in development, and refuses to start outside it.
        foreach (var blank in new[] { "", "   ", null })
        {
            var options = Configured();
            typeof(SmtpEmailOptions).GetProperty(missing)!.SetValue(options, blank);

            options.IsConfigured.ShouldBeFalse();
        }
    }

    private static List<ValidationResult> Validate(SmtpEmailOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, true);
        return results;
    }

    [Fact]
    public void No_relay_at_all_is_a_valid_configuration() =>
        // How a checkout runs without an account: blank credentials select the transport that
        // writes messages to the log.
        Validate(new SmtpEmailOptions { Host = "", Username = "", Password = "", FromEmail = "noreply@example.test" })
            .ShouldBeEmpty();

    [Fact]
    public void A_complete_relay_is_a_valid_configuration() => Validate(Configured()).ShouldBeEmpty();

    [Theory]
    [InlineData("Host")]
    [InlineData("Username")]
    [InlineData("Password")]
    public void Half_a_relay_is_refused_at_startup(string missing)
    {
        // Read as "not configured" it would swallow every message the application sends,
        // silently. ValidateDataAnnotations runs IValidatableObject, so this stops the host.
        var options = Configured();
        typeof(SmtpEmailOptions).GetProperty(missing)!.SetValue(options, "");

        Validate(options).ShouldHaveSingleItem()
            .ErrorMessage.ShouldNotBeNull().ShouldContain(missing);
    }

    [Theory]
    [InlineData(587, SmtpSecurity.StartTls)]
    [InlineData(2587, SmtpSecurity.StartTls)]
    [InlineData(25, SmtpSecurity.StartTls)]
    [InlineData(465, SmtpSecurity.ImplicitTls)]
    [InlineData(2465, SmtpSecurity.ImplicitTls)]
    public void A_port_matched_to_its_handshake_is_accepted(int port, SmtpSecurity security) =>
        Validate(Configured(port, security)).ShouldBeEmpty();

    [Theory]
    [InlineData(1025)]
    [InlineData(2525)]
    [InlineData(587)]
    public void An_unencrypted_relay_answers_on_whatever_port_it_likes(int port) =>
        // No convention to check against: it is somebody's own container. Whether plaintext
        // is allowed at all is decided where the environment is known, not here.
        Validate(Configured(port, SmtpSecurity.None)).ShouldBeEmpty();

    [Theory]
    [InlineData(465, SmtpSecurity.StartTls)]
    [InlineData(2465, SmtpSecurity.StartTls)]
    [InlineData(587, SmtpSecurity.ImplicitTls)]
    public void A_port_that_contradicts_its_handshake_is_refused(int port, SmtpSecurity security) =>
        // Neither side errors: the client waits for a greeting that needs TLS first, or
        // offers TLS to a server already speaking it. It ends as a timeout several retries
        // later, with nothing on screen naming the cause.
        Validate(Configured(port, security)).ShouldHaveSingleItem()
            .ErrorMessage.ShouldNotBeNull().ShouldContain("TLS");

    [Fact]
    public void Revocation_is_required_unless_an_environment_says_otherwise() =>
        // The default is the deployed stance, not the convenient one: Development turns it
        // off because macOS cannot complete the check, and that override is written down.
        new SmtpEmailOptions().RequireCertificateRevocation.ShouldBeTrue();

    [Fact]
    public void Encryption_is_the_default_and_must_be_opted_out_of() =>
        // None has to be written down. A missing setting can never mean plaintext.
        new SmtpEmailOptions().Security.ShouldBe(SmtpSecurity.StartTls);

    [Fact]
    public void The_default_port_is_the_submission_one() =>
        // 587 with STARTTLS. Port 25 is accepted by relays but blocked on egress by most
        // hosting providers, so defaulting to it would fail only once deployed.
        new SmtpEmailOptions().Port.ShouldBe(587);

    [Fact]
    public void The_attempt_timeout_leaves_room_for_retries_inside_the_total()
    {
        // The pipeline nests a per-attempt timeout inside the total one; an attempt allowed
        // to outlast the whole send would make the retry count meaningless.
        SmtpEmailTransport.AttemptTimeout.ShouldBeLessThan(SmtpEmailTransport.TotalTimeout);
    }
}
