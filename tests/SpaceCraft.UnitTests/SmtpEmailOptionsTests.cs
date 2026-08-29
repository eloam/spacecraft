using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SmtpEmailOptionsTests
{
    private static SmtpEmailOptions Configured() => new()
    {
        Host = "smtp.tem.scaleway.com",
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
