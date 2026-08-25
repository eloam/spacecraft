using SpaceCraft.Pipeline.Sandbox;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class OutputRedactorTests
{
    [Fact]
    public void Known_secrets_are_replaced_everywhere_they_appear()
    {
        const string Token = "npm_supersecrettoken12345";
        var output = $"publishing with {Token}\nretrying with {Token}";

        var redacted = OutputRedactor.Redact(output, [Token]);

        redacted.ShouldNotContain(Token);
        redacted.Split(OutputRedactor.Mask).Length.ShouldBe(3);
    }

    [Fact]
    public void An_npm_token_is_caught_even_when_it_was_never_declared()
    {
        // The registry CLI can echo a token we never put in the redaction list.
        var output = "error: bad credentials npm_abcdefghijklmnopqrstuvwxyz0123456789";

        OutputRedactor.Redact(output, []).ShouldNotContain("npm_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    [Fact]
    public void A_github_token_is_caught_by_pattern() =>
        OutputRedactor.Redact("token ghp_abcdefghijklmnopqrstuvwxyz0123456789", [])
            .ShouldNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");

    [Theory]
    [InlineData("_authToken=abc123secret")]
    [InlineData("Authorization: Bearer abc123secret")]
    [InlineData("password = abc123secret")]
    [InlineData("api_key: abc123secret")]
    public void Key_value_secrets_are_masked(string line) =>
        OutputRedactor.Redact(line, []).ShouldNotContain("abc123secret");

    [Fact]
    public void Short_values_are_not_treated_as_secrets()
    {
        // Redacting a 3-character "secret" would blank out unrelated text.
        var output = "built 42 files in dist";

        OutputRedactor.Redact(output, ["42"]).ShouldBe(output);
    }

    [Fact]
    public void Ordinary_build_output_survives_untouched()
    {
        const string Output = "added 11 packages in 2s\n> tsc\nacme-petstore-sdk-1.0.0.tgz";

        OutputRedactor.Redact(Output, []).ShouldBe(Output);
    }
}
