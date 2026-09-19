using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Where the reset link goes.
/// </summary>
/// <remarks>
/// The account is found by normalised address, so a request spelled differently from the
/// registration still finds it. The mail used to go to the spelling in the request; it is a
/// bearer credential, and it belongs to the address the account actually holds.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class PasswordResetTests(SpaceCraftApi api)
{
    [Fact]
    public async Task The_link_is_mailed_to_the_address_on_the_account_not_the_one_typed()
    {
        var stored = $"{Guid.NewGuid():N}@example.test";
        var client = await api.SignedInAsync(stored);

        var typed = stored.ToUpperInvariant();
        var asked = await client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = typed },
            TestContext.Current.CancellationToken);

        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The mailbox matches addresses case-insensitively, which is why the recipient is
        // asserted separately: the message exists either way, and only its To differs.
        var message = await api.Mailbox.WaitForAsync(typed, "password", TestContext.Current.CancellationToken);

        message.To.ShouldBe(stored);
    }
}
