using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// The API, its database, and nothing stubbed between them.
/// </summary>
/// <remarks>
/// These tests exist for the seams unit tests cannot reach: tenant isolation, the shape of a
/// refusal, whether a value written through an endpoint is really encrypted in the column,
/// and whether a URL still resolves to the row it named. Every one of those is a claim about
/// the whole request path — route, middleware, guard, query — and each has been wrong at
/// least once in a way no unit test would have noticed.
/// <para>
/// Postgres comes from a container rather than the development database: a suite that leaves
/// rows behind in the database someone is working in gets run less and less.
/// </para>
/// </remarks>
public sealed class SpaceCraftApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("spacecraft")
        .WithUsername("spacecraft")
        .WithPassword("spacecraft")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Touching Services forces the host to build, so the schema exists before the first
        // request rather than on whichever test happens to run first.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, deliberately: it is the one environment that allows an unwrapped key
        // ring, and wrapping has its own tests. Asking every integration test to stand up a
        // key manager would buy nothing and cost a container.
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("KeyProtection:Provider", "None");
        builder.UseSetting("Database:MigrateOnStartup", "false");

        // Every test registers its own account, and the credential endpoints are deliberately
        // throttled per address — which in a test host is one address for the whole suite, so
        // the tenth test would be refused for reasons that have nothing to do with what it
        // asserts. Raised rather than removed, so a runaway loop still stops.
        builder.UseSetting("RateLimits:CredentialPermitsPerWindow", "10000");
        builder.UseSetting("RateLimits:GlobalPermitsPerMinute", "100000");
    }

    /// <summary>A client already signed in as a brand-new account.</summary>
    public async Task<HttpClient> SignedInAsync(string email)
    {
        var client = CreateClient();

        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = "Correct-Horse-Battery-9!", displayName = email.Split('@')[0] },
            TestContext.Current.CancellationToken);

        registered.EnsureSuccessStatusCode();

        var session = await registered.Content.ReadFromJsonAsync<AuthPayload>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    /// <summary>Only what the tests read back; the response carries more.</summary>
    private sealed record AuthPayload(string AccessToken);
}

/// <summary>
/// One API and one database for the whole suite. Registering a user per test keeps them
/// independent without paying for a container each time.
/// </summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<SpaceCraftApi>;
