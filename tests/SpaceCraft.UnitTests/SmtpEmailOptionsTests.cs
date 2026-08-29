using SpaceCraft.Api.Email;
using SpaceCraft.Infrastructure.Scaleway;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SmtpEmailOptionsTests
{
    /// <summary>
    /// A relay to talk to and a password to say it with. The username is not part of it: it
    /// defaults to the account's project id, so requiring it here would refuse a perfectly
    /// good Scaleway configuration.
    /// </summary>
    [Fact]
    public void A_host_and_a_password_are_what_make_the_transport_usable() =>
        new SmtpEmailOptions { Host = "smtp.tem.scaleway.com", Password = "secret" }
            .IsConfigured.ShouldBeTrue();

    [Fact]
    public void A_project_id_is_what_makes_the_account_usable() =>
        new ScalewayOptions { ProjectId = "project" }.IsConfigured.ShouldBeTrue();

    [Theory]
    [InlineData("", "secret")]
    [InlineData("   ", "secret")]
    [InlineData(null, "secret")]
    [InlineData("smtp.tem.scaleway.com", "")]
    [InlineData("smtp.tem.scaleway.com", "   ")]
    [InlineData("smtp.tem.scaleway.com", null)]
    public void Half_a_configuration_does_not_count(string? host, string? password) =>
        new SmtpEmailOptions { Host = host, Password = password }.IsConfigured.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_project_id_does_not_count_as_configured(string? value) =>
        new ScalewayOptions { ProjectId = value }.IsConfigured.ShouldBeFalse();

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
