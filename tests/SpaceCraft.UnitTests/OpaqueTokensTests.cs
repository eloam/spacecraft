using SpaceCraft.Api.Common;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The one shape every raw token has, and the one hash every stored one is.
/// </summary>
/// <remarks>
/// Refresh tokens, reset links, invitations and waitlist confirmations all come from here now,
/// where they used to come from three copies in two encodings. The hash is pinned to a known
/// SHA-256 vector because rows already hold hashes of tokens issued before the copies merged:
/// a change here would make every outstanding link and session stop redeeming.
/// </remarks>
public sealed class OpaqueTokensTests
{
    [Fact]
    public void A_token_is_256_bits_as_lowercase_hex()
    {
        // Hex rather than base64url: nothing to escape in a link or a JSON body, and no
        // character a mail client's link matcher might stop at.
        OpaqueTokens.Generate().ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Tokens_do_not_repeat()
    {
        Enumerable.Range(0, 1000).Select(_ => OpaqueTokens.Generate()).ShouldBeUnique();
    }

    [Fact]
    public void The_hash_is_sha256_of_the_utf8_bytes_in_lowercase_hex()
    {
        // The published SHA-256 of "abc".
        OpaqueTokens.Hash("abc")
            .ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void A_blank_value_cannot_be_hashed()
    {
        // A lookup by the hash of nothing would find the row of nothing; refusing here keeps
        // that from ever being a query.
        Should.Throw<ArgumentException>(() => OpaqueTokens.Hash(" "));
    }
}
