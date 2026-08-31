using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceCraft.Domain.Registries;

/// <summary>
/// What a registry asks to be shown at publish time.
/// </summary>
/// <remarks>
/// Held as a JSON document inside the credential's ciphertext rather than as columns beside
/// it, because the shape differs by registry and adding a column later would mean migrating
/// encrypted data — decrypting and re-encrypting every row, which is the one operation worth
/// designing away in advance. npm and NuGet authenticate with a token alone; Maven Central,
/// Nexus and Artifactory want a user with it; PyPI wants the literal user <c>__token__</c>
/// beside its API token.
/// <para>
/// The username is inside the encrypted document with the token rather than in a plain
/// column. It is not itself a secret, but it is half of one, and splitting a credential
/// across two storage shapes invites the half that looks harmless to be logged, listed or
/// returned by an endpoint that only meant to describe the credential.
/// </para>
/// </remarks>
public sealed record RegistrySecret(string Token, string? Username = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Reads what was stored, in either shape it may have been written.
    /// </summary>
    /// <remarks>
    /// Credentials written before this document existed hold the bare token, so anything that
    /// is not a JSON object naming a token is taken to be the token itself. That is what lets
    /// every credential already stored stay readable without a migration having to run first:
    /// the rewrite normalises them, and nothing breaks while it has not.
    /// </remarks>
    public static RegistrySecret Parse(string stored)
    {
        ArgumentException.ThrowIfNullOrEmpty(stored);

        if (stored[0] == '{')
        {
            try
            {
                if (JsonSerializer.Deserialize<RegistrySecret>(stored, Json) is { } parsed
                    && !string.IsNullOrEmpty(parsed.Token))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // A token that merely starts with a brace. Falls through to the legacy shape.
            }
        }

        return new RegistrySecret(stored);
    }

    /// <summary>The tail shown in listings, so a credential can be told apart without it.</summary>
    public string LastFour() => Token.Length <= 4 ? Token : Token[^4..];
}
