using System.ComponentModel.DataAnnotations;
using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SmtpEmailOptionsTests
{
    private static SmtpEmailOptions Configured(int port = 587, bool implicitTls = false) => new()
    {
        Host = "smtp.tem.scaleway.com",
        Port = port,
        UseImplicitTls = implicitTls,
        Username = "a-project-id",
        Password = "secret",
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
        Validate(new SmtpEmailOptions { Host = "", Username = "", Password = "" })
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
    [InlineData(587, false)]
    [InlineData(2587, false)]
    [InlineData(25, false)]
    [InlineData(465, true)]
    [InlineData(2465, true)]
    public void A_port_matched_to_its_handshake_is_accepted(int port, bool implicitTls) =>
        Validate(Configured(port, implicitTls)).ShouldBeEmpty();

    [Theory]
    [InlineData(465, false)]
    [InlineData(2465, false)]
    [InlineData(587, true)]
    public void A_port_that_contradicts_its_handshake_is_refused(int port, bool implicitTls) =>
        // Neither side errors: the client waits for a greeting that needs TLS first, or
        // offers TLS to a server already speaking it. It ends as a timeout several retries
        // later, with nothing on screen naming the cause.
        Validate(Configured(port, implicitTls)).ShouldHaveSingleItem()
            .ErrorMessage.ShouldNotBeNull().ShouldContain("TLS");

    [Fact]
    public void The_default_port_is_the_submission_one() =>
        // 587 with STARTTLS. Port 25 is accepted by relays but blocked on egress by most
        // hosting providers, so defaulting to it would fail only once deployed.
        new SmtpEmailOptions().Port.ShouldBe(587);

    [Fact]
    public void The_attempt_timeout_leaves_room_for_retries_inside_the_total()
    {
        var options = new SmtpEmailOptions();

        // The pipeline nests a per-attempt timeout inside the total one; an attempt allowed
        // to outlast the whole send would make the retry count meaningless.
        options.AttemptTimeout.ShouldBeLessThan(options.Timeout);
    }
}
