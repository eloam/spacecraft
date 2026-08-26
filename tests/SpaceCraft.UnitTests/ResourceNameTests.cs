using SpaceCraft.Domain.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class ResourceNameTests
{
    [Theory]
    [InlineData("Acme Corp")]
    [InlineData("Pet Store")]
    [InlineData("keycloak-admin")]
    [InlineData("Load Test 3")]
    [InlineData("API")]
    [InlineData("2fa Service")]
    public void Ordinary_names_are_accepted(string name) =>
        ResourceName.IsValid(name).ShouldBeTrue();

    [Theory]
    [InlineData("Café API")]          // accents would transliterate, changing the URL silently
    [InlineData("Billing/v2")]
    [InlineData("Acme, Inc.")]
    [InlineData("emoji 🚀")]
    [InlineData("under_score")]
    public void Characters_that_would_not_survive_slugging_are_refused(string name) =>
        ResourceName.IsValid(name).ShouldBeFalse();

    [Fact]
    public void A_name_must_start_with_a_letter_or_number() =>
        // A leading hyphen would produce a slug starting with one, which reads as an option
        // flag in a shell and is stripped anyway.
        ResourceName.IsValid("-leading-hyphen").ShouldBeFalse();

    [Fact]
    public void Leading_whitespace_is_trimmed_rather_than_rejected() =>
        ResourceName.IsValid(" leading space").ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_names_are_refused(string? name) =>
        ResourceName.IsValid(name).ShouldBeFalse();

    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_rejected() =>
        ResourceName.Require("  Acme Corp  ").ShouldBe("Acme Corp");

    [Fact]
    public void A_name_over_the_limit_is_refused() =>
        ResourceName.IsValid(new string('a', ResourceName.MaxLength + 1)).ShouldBeFalse();
}
