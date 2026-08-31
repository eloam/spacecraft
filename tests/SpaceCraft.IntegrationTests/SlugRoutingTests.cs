using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Whether a URL still names the row it appears to name.
/// </summary>
/// <remarks>
/// The slug cache rested on "slugs are immutable once created", which the domain contradicts:
/// renaming recomputes the slug. For as long as an entry lived, a freed slug taken by a
/// sibling resolved to its previous owner — a request naming one project answered, and
/// written to, by another. That is not visible from either the middleware or the interceptor
/// alone; it only appears when a rename and a request meet on the same running system.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class SlugRoutingTests(SpaceCraftApi api)
{
    [Fact]
    public async Task A_renamed_project_stops_answering_to_the_name_it_left_behind()
    {
        var (client, organizationSlug) = await OrganizationAsync();

        var project = await CreateProjectAsync(client, organizationSlug, "Billing API");
        project.Slug.ShouldBe("billing-api");

        // Resolve once, which is what puts the mapping in the cache.
        (await client.GetAsync(Specs(organizationSlug, "billing-api"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationSlug}/projects/{project.Id}", UriKind.Relative),
            new { name = "Invoicing API" },
            TestContext.Current.CancellationToken);

        renamed.EnsureSuccessStatusCode();

        // The old name must stop resolving immediately, not when the entry would have lapsed.
        (await client.GetAsync(Specs(organizationSlug, "billing-api"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await client.GetAsync(Specs(organizationSlug, "invoicing-api"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_slug_probed_before_it_existed_answers_as_soon_as_it_does()
    {
        var (client, organizationSlug) = await OrganizationAsync();

        // Probing records that the slug resolves to nothing. Cached without an eviction, that
        // absence would outlive the creation below and hide a project from its own name.
        (await client.GetAsync(Specs(organizationSlug, "ledger"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await CreateProjectAsync(client, organizationSlug, "Ledger");

        (await client.GetAsync(Specs(organizationSlug, "ledger"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Uri Specs(string organizationSlug, string projectSlug) =>
        new($"/api/v1/organizations/{organizationSlug}/projects/{projectSlug}/specs", UriKind.Relative);

    private static async Task<ProjectPayload> CreateProjectAsync(
        HttpClient client, string organizationSlug, string name)
    {
        var created = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationSlug}/projects", UriKind.Relative),
            new { name },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        return (await created.Content.ReadFromJsonAsync<ProjectPayload>(TestContext.Current.CancellationToken))!;
    }

    private async Task<(HttpClient Client, string Slug)> OrganizationAsync()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");

        var created = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var body = await created.Content.ReadFromJsonAsync<OrganizationPayload>(TestContext.Current.CancellationToken);
        return (client, body!.Slug);
    }

    private sealed record OrganizationPayload(Guid Id, string Slug);

    private sealed record ProjectPayload(Guid Id, string Slug);
}
