using SpaceCraft.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The placeholder is what appsettings.json uses to say "the deployment supplies this". It is
/// also an ordinary string, so nothing else in the stack would ever object to it.
/// </summary>
public class RequiredSettingsTests
{
    private static IConfiguration From(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void A_configuration_with_nothing_left_to_supply_passes() =>
        Should.NotThrow(() => From(
            ("Email:Smtp:Password", "secret"),
            ("ObjectStorage:AccessKey", ""),
            ("Jwt:SigningKey", null)).ThrowIfPlaceholdersRemain());

    [Fact]
    public void An_unreplaced_placeholder_stops_startup()
    {
        var error = Should.Throw<InvalidOperationException>(
            () => From(("Email:Smtp:Password", RequiredSettings.Placeholder)).ThrowIfPlaceholdersRemain());

        error.Message.ShouldContain("Email:Smtp:Password");
        // Says how to set it, because the environment spells the key differently.
        error.Message.ShouldContain("Email__Smtp__Password");
    }

    [Fact]
    public void Every_offending_key_is_named_at_once()
    {
        // Finding them one restart at a time is how a five-minute deployment becomes an hour.
        var error = Should.Throw<InvalidOperationException>(() => From(
            ("Email:Smtp:Password", RequiredSettings.Placeholder),
            ("Email:Smtp:Username", RequiredSettings.Placeholder),
            ("ObjectStorage:SecretKey", RequiredSettings.Placeholder)).ThrowIfPlaceholdersRemain());

        error.Message.ShouldContain("Email:Smtp:Password");
        error.Message.ShouldContain("Email:Smtp:Username");
        error.Message.ShouldContain("ObjectStorage:SecretKey");
        error.Message.ShouldContain("3 settings");
    }

    [Theory]
    [InlineData("[required]")]
    [InlineData("[REQUIRED] ")]
    [InlineData("REQUIRED")]
    public void Only_the_exact_marker_counts(string value) =>
        // A value that merely resembles it is someone's real setting, and refusing to start
        // over it would be worse than the problem this solves.
        Should.NotThrow(() => From(("Email:Smtp:Password", value)).ThrowIfPlaceholdersRemain());
}
