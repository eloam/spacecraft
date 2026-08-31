using SpaceCraft.Domain.Registries;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The hinge between what is stored today and what was stored before the document existed.
/// </summary>
/// <remarks>
/// Every credential in the database predates this shape. If reading them depended on a
/// migration having run, the migration would be load-bearing on the first request after
/// deploy — so it reads both, and the rewrite only normalises.
/// </remarks>
public class RegistrySecretTests
{
    [Fact]
    public void A_credential_written_as_a_bare_token_is_still_readable()
    {
        RegistrySecret.Parse("npm_AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRr")
            .ShouldBe(new RegistrySecret("npm_AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRr"));
    }

    [Fact]
    public void A_token_and_a_user_survive_the_round_trip()
    {
        var secret = new RegistrySecret("a-verdaccio-token", "ci-publisher");

        RegistrySecret.Parse(secret.ToJson()).ShouldBe(secret);
    }

    [Fact]
    public void A_document_without_a_user_writes_no_user()
    {
        // Absent rather than null: the stored bytes are what a key manager encrypts, and
        // there is no reason to pay for a field nothing reads.
        new RegistrySecret("token-only").ToJson().ShouldBe("""{"token":"token-only"}""");
    }

    [Fact]
    public void A_token_that_merely_begins_with_a_brace_is_a_token()
    {
        // The shape test is cheap and wrong on its own; parsing has to decide.
        RegistrySecret.Parse("{not-json-at-all").Token.ShouldBe("{not-json-at-all");
    }

    [Fact]
    public void Json_that_names_no_token_is_taken_as_the_token_itself()
    {
        RegistrySecret.Parse("""{"username":"only"}""").Token.ShouldBe("""{"username":"only"}""");
    }

    [Theory]
    [InlineData("npm_AaBbCcDd", "cCdD")]
    [InlineData("abc", "abc")]
    public void The_tail_shown_in_listings_never_reads_past_the_token(string token, string expected)
    {
        new RegistrySecret(token).LastFour().ShouldBe(expected, StringCompareShould.IgnoreCase);
    }
}
