using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// What actually lands in the column when a credential is stored.
/// </summary>
/// <remarks>
/// The claim "tokens are encrypted at rest" is the kind that stays true in a comment long
/// after it has stopped being true in the code. It is worth asserting against the column
/// itself, through the real endpoint, rather than against the protector in isolation.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class CredentialStorageTests(SpaceCraftApi api)
{
    // Shaped like an automation token, which the endpoint requires for the public registry.
    private const string Token = "npm_IntegrationTestTokenAaBbCcDdEeFfGgHh";

    [Fact]
    public async Task The_token_is_nowhere_in_the_row_that_stores_it()
    {
        var (client, organizationId) = await OrganizationAsync();

        var created = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/credentials", UriKind.Relative),
            new { registry = 1, label = "npm automation", token = Token },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var stored = await db.RegistryCredentials
            .SingleAsync(c => c.OrganizationId == organizationId, TestContext.Current.CancellationToken);

        // Not "does not equal": the bytes must not contain it anywhere, which also catches a
        // ciphertext that merely prefixes or pads the value it was meant to hide.
        Encoding.UTF8.GetString(stored.CipherText).ShouldNotContain("npm_");
        stored.LastFour.ShouldBe(Token[^4..]);

        // The response describes the credential without ever carrying the secret.
        (await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldNotContain(Token);
    }

    [Fact]
    public async Task A_user_travels_with_the_token_inside_the_encrypted_document()
    {
        var (client, organizationId) = await OrganizationAsync();

        var created = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/credentials", UriKind.Relative),
            new
            {
                registry = 1,
                label = "private registry",
                token = "a-verdaccio-issued-token-value",
                username = "ci-publisher",
                // A registry the organization runs: its token is not npm's shape, and must
                // not be judged as though it were.
                registryUrl = "https://npm.acme.internal",
            },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var stored = await db.RegistryCredentials
            .SingleAsync(c => c.OrganizationId == organizationId, TestContext.Current.CancellationToken);

        var secret = RegistrySecret.Parse(protector.Unprotect(stored.CipherText));

        secret.Token.ShouldBe("a-verdaccio-issued-token-value");
        secret.Username.ShouldBe("ci-publisher");

        // The username is inside the encrypted document, not in a column beside it: half a
        // credential is still credential material.
        Encoding.UTF8.GetString(stored.CipherText).ShouldNotContain("ci-publisher");
    }

    private async Task<(HttpClient Client, Guid OrganizationId)> OrganizationAsync()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");

        var created = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org {Guid.NewGuid():N}"[..12] },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var body = await created.Content.ReadFromJsonAsync<OrganizationPayload>(TestContext.Current.CancellationToken);
        return (client, body!.Id);
    }

    private sealed record OrganizationPayload(Guid Id);
}
