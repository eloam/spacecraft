using SpaceCraft.Api.Email;
using SpaceCraft.Infrastructure.Scaleway;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class ScalewayEmailOptionsTests
{
    /// <summary>
    /// Sending still needs a key and a project id, but they come from two sections now: the
    /// key belongs to this service, the project to the account. Each half only answers for
    /// itself; the composition root requires both together.
    /// </summary>
    [Fact]
    public void A_key_is_what_makes_the_transport_usable() =>
        new ScalewayEmailOptions { ApiKey = "secret" }.IsConfigured.ShouldBeTrue();

    [Fact]
    public void A_project_id_is_what_makes_the_account_usable() =>
        new ScalewayOptions { ProjectId = "project" }.IsConfigured.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_credentials_do_not_count_as_configured(string? value)
    {
        new ScalewayEmailOptions { ApiKey = value }.IsConfigured.ShouldBeFalse();
        new ScalewayOptions { ProjectId = value }.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void The_region_defaults_to_where_the_service_exists() =>
        // Transactional Email is Paris-only; defaulting elsewhere fails at the first send.
        new ScalewayEmailOptions().Region.ShouldBe("fr-par");

    [Fact]
    public void The_attempt_timeout_leaves_room_for_retries_inside_the_total()
    {
        var options = new ScalewayEmailOptions();

        // The resilience pipeline refuses to build if a single attempt could outlast the
        // whole request, so this relationship has to hold for the app to start at all.
        options.AttemptTimeout.ShouldBeLessThan(options.Timeout);
    }
}
