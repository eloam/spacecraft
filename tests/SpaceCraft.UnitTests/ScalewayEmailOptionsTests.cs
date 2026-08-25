using SpaceCraft.Api.Email;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class ScalewayEmailOptionsTests
{
    [Fact]
    public void Both_a_key_and_a_project_are_needed_before_the_transport_is_usable()
    {
        // Half-configured is the dangerous state: it looks deliberate but cannot send.
        new ScalewayEmailOptions { ApiKey = "secret" }.IsConfigured.ShouldBeFalse();
        new ScalewayEmailOptions { ProjectId = "project" }.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void A_fully_configured_transport_is_recognised() =>
        new ScalewayEmailOptions { ApiKey = "secret", ProjectId = "project" }
            .IsConfigured.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_credentials_do_not_count_as_configured(string? value) =>
        new ScalewayEmailOptions { ApiKey = value, ProjectId = value }.IsConfigured.ShouldBeFalse();

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
