using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Whether an account actually locks.
/// </summary>
/// <remarks>
/// It did not, for the whole life of the feature. Identity was configured with a ten-attempt
/// limit and a fifteen-minute lockout, a comment in the rate limiter leaned on it in writing,
/// and the login handler checked <c>IsLockedOutAsync</c> — but nothing ever recorded a failure,
/// so the count stayed at zero and that branch was unreachable. Configuration that describes a
/// protection nobody armed is worse than none: it is read as a decision already taken.
/// <para>
/// The rate limiter is deliberately not the thing under test here. It is raised out of the way
/// in this host, so what a run of this test measures is the lockout alone.
/// </para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class LockoutTests(SpaceCraftApi api)
{
    private const string Password = "Correct-Horse-Battery-9!";

    /// <summary>Matches MaxFailedAccessAttempts in the Identity options.</summary>
    private const int Attempts = 10;

    [Fact]
    public async Task An_account_locks_after_the_configured_number_of_failures()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var client = await api.SignedInAsync(email);

        for (var i = 0; i < Attempts; i++)
        {
            var refused = await LoginAsync(client, email, "not-the-password");
            refused.StatusCode.ShouldBe(
                HttpStatusCode.Unauthorized,
                $"attempt {i + 1} should be refused as a bad password, not as a lockout");
        }

        // The password is right this time, and it must not matter. A lockout that only refuses
        // wrong guesses stops nothing: the attacker's last guess is the correct one.
        var locked = await LoginAsync(client, email, Password);

        locked.StatusCode.ShouldBe(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task A_password_that_works_clears_the_tally()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var client = await api.SignedInAsync(email);

        // Nine failures then a success, twice over. Without a reset the second round would
        // cross the limit and lock an account whose owner never mistyped twice in a row.
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < Attempts - 1; i++)
            {
                (await LoginAsync(client, email, "not-the-password")).StatusCode
                    .ShouldBe(HttpStatusCode.Unauthorized);
            }

            (await LoginAsync(client, email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            TestContext.Current.CancellationToken);
}

/// <summary>
/// What a stolen refresh token is worth.
/// </summary>
/// <remarks>
/// Rotation only protects anything if presenting a token twice is *noticed*. Redemption read
/// the row, checked it was unspent, and marked it spent forty lines later — so two requests
/// carrying the same token both passed the check, and one family quietly became two live
/// chains. From then on the reuse detection could never fire for that family, which is to say
/// the stolen token had become a permanent session.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class RefreshReuseTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Presenting_one_refresh_token_twice_revokes_the_whole_family()
    {
        var client = api.CreateClient();
        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = $"{Guid.NewGuid():N}@example.test",
                password = "Correct-Horse-Battery-9!",
                displayName = "Reuse",
            },
            TestContext.Current.CancellationToken);

        registered.EnsureSuccessStatusCode();
        var session = await registered.Content.ReadFromJsonAsync<Tokens>(TestContext.Current.CancellationToken);

        // The legitimate holder rotates once.
        var first = await RefreshAsync(client, session!.RefreshToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The attacker replays the copy they took before the rotation.
        var replay = await RefreshAsync(client, session.RefreshToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the family goes with it, so what the attacker took is worthless and the
        // legitimate holder is told to sign in again.
        var rotated = await first.Content.ReadFromJsonAsync<Tokens>(TestContext.Current.CancellationToken);
        (await RefreshAsync(client, rotated!.RefreshToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_requests_carrying_the_same_token_do_not_both_succeed()
    {
        var client = api.CreateClient();
        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = $"{Guid.NewGuid():N}@example.test",
                password = "Correct-Horse-Battery-9!",
                displayName = "Race",
            },
            TestContext.Current.CancellationToken);

        registered.EnsureSuccessStatusCode();
        var session = await registered.Content.ReadFromJsonAsync<Tokens>(TestContext.Current.CancellationToken);

        // Sent together, which is what a replay racing the real holder looks like. Exactly one
        // may win: two winners is one family split into two chains that can never be caught.
        var both = await Task.WhenAll(
            RefreshAsync(client, session!.RefreshToken),
            RefreshAsync(client, session.RefreshToken));

        both.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken },
            TestContext.Current.CancellationToken);

    private sealed record Tokens(string AccessToken, string RefreshToken);
}
