using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// What an upload and a revalidation read from a project's history, now that neither loads it.
/// </summary>
/// <remarks>
/// Three things were computed over the whole history in memory and are now asked of the
/// database: the next sequence number, the document a duplicate upload is byte-for-byte a
/// copy of, and the version the strict policy compares the declared one with — the newest
/// for an upload, the one immediately before for a revalidation. Each is a claim about which
/// row a query returns, so each is made against the real database, through the endpoints.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class SpecHistoryTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Uploads_are_numbered_from_the_newest_version_and_judged_against_it()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);

        var first = await UploadAsync(client, organizationId, projectId, Document("1.0.0"));
        var second = await UploadAsync(client, organizationId, projectId, Document("1.1.0"));
        var third = await UploadAsync(client, organizationId, projectId, Document("1.2.0"));

        first.SpecVersion.Sequence.ShouldBe(1);
        second.SpecVersion.Sequence.ShouldBe(2);
        third.SpecVersion.Sequence.ShouldBe(3);

        // Judged against #3, the newest, and not against #1 or nothing at all: the same
        // version with different bytes is a document a registry will refuse.
        var repeated = await UploadAsync(
            client, organizationId, projectId, Document("1.2.0", "Invoices, payments and refunds."));

        repeated.SpecVersion.Sequence.ShouldBe(4);
        repeated.Diagnostics.ShouldContain(d => d.Message.Contains("still '1.2.0'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_duplicate_is_refused_naming_the_version_it_copies()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);

        await UploadAsync(client, organizationId, projectId, Document("1.0.0"));
        await UploadAsync(client, organizationId, projectId, Document("1.1.0"));
        await UploadAsync(client, organizationId, projectId, Document("1.2.0"));

        var refused = await PostAsync(client, organizationId, projectId, Document("1.1.0"));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<ProblemPayload>(Ct))!.Detail.ShouldNotBeNull()
            .ShouldContain("spec #2");
    }

    [Fact]
    public async Task Revalidation_judges_a_document_against_the_one_immediately_before_it()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);

        var first = await UploadAsync(client, organizationId, projectId, Document("1.0.0"));
        var second = await UploadAsync(client, organizationId, projectId, Document("1.0.0", "Invoices only."));
        await UploadAsync(client, organizationId, projectId, Document("1.1.0"));

        // #2 declares the version #1 did: an error against #1, which is the document before
        // it — and not against #3, the newest, which would let it pass.
        var revalidated = await RevalidateAsync(client, organizationId, projectId, second.SpecVersion.Id);
        revalidated.Diagnostics.ShouldContain(d => d.Message.Contains("still '1.0.0'", StringComparison.Ordinal));

        // #1 has nothing before it, so nothing to be judged against.
        var earliest = await RevalidateAsync(client, organizationId, projectId, first.SpecVersion.Id);
        earliest.Diagnostics.ShouldNotContain(d => d.Message.Contains("still '1.0.0'", StringComparison.Ordinal));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Complete enough for strict validation, which projects run by default, to have nothing
    // to say beyond the version rule under test.
    private static string Document(string version, string description = "Invoices and payments.") => $"""
        openapi: 3.0.3
        info:
          title: Billing API
          version: {version}
          description: {description}
          license:
            name: MIT
        servers:
          - url: https://api.example.test
        paths:
          /invoices:
            get:
              operationId: listInvoices
              responses:
                '200':
                  description: ok
        """;

    private static async Task<UploadPayload> UploadAsync(
        HttpClient client, Guid organizationId, Guid projectId, string document)
    {
        var uploaded = await PostAsync(client, organizationId, projectId, document);
        uploaded.EnsureSuccessStatusCode();

        return (await uploaded.Content.ReadFromJsonAsync<UploadPayload>(Ct))!;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, Guid organizationId, Guid projectId, string document)
    {
        using var content = new MultipartFormDataContent();
        using var file = new StringContent(document);
        content.Add(file, "file", "openapi.yaml");

        return await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            Ct);
    }

    private static async Task<UploadPayload> RevalidateAsync(
        HttpClient client, Guid organizationId, Guid projectId, Guid specVersionId)
    {
        var revalidated = await client.PostAsJsonAsync(
            new Uri(
                $"/api/v1/organizations/{organizationId}/projects/{projectId}/specs/{specVersionId}/revalidate",
                UriKind.Relative),
            new { },
            Ct);

        revalidated.EnsureSuccessStatusCode();

        return (await revalidated.Content.ReadFromJsonAsync<UploadPayload>(Ct))!;
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId)> ProjectAsync(HttpClient client)
    {
        var organization = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            Ct);

        organization.EnsureSuccessStatusCode();
        var org = await organization.Content.ReadFromJsonAsync<IdPayload>(Ct);

        var project = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{org!.Id}/projects", UriKind.Relative),
            new { name = "Billing API" },
            Ct);

        project.EnsureSuccessStatusCode();
        var created = await project.Content.ReadFromJsonAsync<IdPayload>(Ct);

        return (org.Id, created!.Id);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record SpecPayload(Guid Id, int Sequence);

    private sealed record DiagnosticPayload(string Message);

    private sealed record UploadPayload(SpecPayload SpecVersion, IReadOnlyList<DiagnosticPayload> Diagnostics);

    private sealed record ProblemPayload(string? Detail);
}
