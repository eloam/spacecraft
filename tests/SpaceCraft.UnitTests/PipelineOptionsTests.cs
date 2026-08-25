using SpaceCraft.Pipeline;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class PipelineOptionsTests
{
    private static PipelineOptions Options(SandboxMode mode) =>
        new() { Sandbox = mode, KiotaImage = "mcr.microsoft.com/openapi/kiota:1.34.1" };

    [Fact]
    public void Running_with_no_isolation_outside_development_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => Options(SandboxMode.None).Validate(isDevelopment: false))
            .Message.ShouldContain("Sandbox is None");
    }

    [Fact]
    public void No_isolation_is_allowed_in_development() =>
        Should.NotThrow(() => Options(SandboxMode.None).Validate(isDevelopment: true));

    [Fact]
    public void Platform_isolation_is_accepted_in_production() =>
        // A serverless job or single-use pod is a deliberate, declared deployment choice —
        // unlike switching isolation off entirely, which stays refused.
        Should.NotThrow(() => Options(SandboxMode.PlatformIsolated).Validate(isDevelopment: false));

    [Fact]
    public void A_pinned_generator_image_is_accepted() =>
        Should.NotThrow(() => Options(SandboxMode.Container).Validate(isDevelopment: false));

    [Theory]
    [InlineData("mcr.microsoft.com/openapi/kiota:latest")]
    [InlineData("mcr.microsoft.com/openapi/kiota:LATEST")]
    [InlineData("mcr.microsoft.com/openapi/kiota")]
    [InlineData("")]
    public void A_floating_generator_tag_is_refused_when_containers_are_used(string image)
    {
        // Kiota 1.29.1 emits getCollectionOfPrimitiveValues<string>() while recommending
        // abstractions preview.103, where that method takes an argument. Every SDK using a
        // primitive array then fails to compile. Pinning is what prevents that pairing.
        var options = new PipelineOptions { Sandbox = SandboxMode.Container, KiotaImage = image };

        Should.Throw<InvalidOperationException>(() => options.Validate(isDevelopment: false))
            .Message.ShouldContain("pinned");
    }

    [Fact]
    public void The_image_tag_is_irrelevant_when_steps_do_not_run_in_containers()
    {
        // PlatformIsolated uses the toolchain baked into the running image; no image is pulled.
        var options = new PipelineOptions { Sandbox = SandboxMode.PlatformIsolated, KiotaImage = "" };

        Should.NotThrow(() => options.Validate(isDevelopment: false));
    }

    [Theory]
    [InlineData("mcr.microsoft.com/openapi/kiota:1.34.1", false)]
    [InlineData("localhost:5000/kiota:1.34.1", false)]
    [InlineData("localhost:5000/kiota", true)]
    [InlineData("kiota:latest", true)]
    public void Registry_ports_are_not_mistaken_for_tags(string image, bool floating) =>
        PipelineOptions.UsesFloatingTag(image).ShouldBe(floating);

    [Theory]
    [InlineData(SandboxMode.Container, true)]
    [InlineData(SandboxMode.PlatformIsolated, false)]
    [InlineData(SandboxMode.None, false)]
    public void Only_the_container_mode_nests_containers(SandboxMode mode, bool expected) =>
        mode.UsesContainers().ShouldBe(expected);
}
