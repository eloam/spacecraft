using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Infrastructure.Identity;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// An access token stops working the moment its session is revoked or its owner's security
/// stamp rotates, not when it expires.
/// </summary>
/// <remarks>
/// Both answers come from one query now, run ahead of every authenticated request, so each
/// is asserted on its own: a session ended with the stamp untouched, and a stamp rotated with
/// the session still alive. A token that fails either is refused before any handler runs.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class TokenLivenessTests(SpaceCraftApi api)
{
    [Fact]
    public async Task A_token_whose_session_was_revoked_is_refused()
    {
        var (client, refreshToken) = await RegisteredAsync();

        (await client.GetAsync(Me, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Logout revokes the refresh-token family the access token names; nothing about the
        // user row changes.
        var loggedOut = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/logout", UriKind.Relative), new { refreshToken }, Ct);
        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync(Me, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_issued_before_the_stamp_rotated_is_refused()
    {
        var (client, _) = await RegisteredAsync();

        var me = await client.GetFromJsonAsync<UserPayload>(Me, Ct);

        // Rotated directly, the way a password change or a forced sign-out does it, with the
        // session's refresh tokens left alive so that the stamp is the only thing refusing.
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SpaceCraftUser>>();
            var user = await users.FindByIdAsync(me!.Id.ToString());
            (await users.UpdateSecurityStampAsync(user!)).Succeeded.ShouldBeTrue();
        }

        (await client.GetAsync(Me, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Uri Me => new("/api/v1/auth/me", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Registered by hand rather than through the fixture, which keeps the access token only:
    /// logging out takes the refresh half.
    /// </summary>
    private async Task<(HttpClient Client, string RefreshToken)> RegisteredAsync()
    {
        var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";

        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = "Correct-Horse-Battery-9!", displayName = email.Split('@')[0] },
            Ct);

        registered.EnsureSuccessStatusCode();

        var session = await registered.Content.ReadFromJsonAsync<AuthPayload>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return (client, session.RefreshToken);
    }

    private sealed record AuthPayload(string AccessToken, string RefreshToken);

    private sealed record UserPayload(Guid Id);
}
