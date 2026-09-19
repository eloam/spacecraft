using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Reading a stored document back, and what happens when the bucket no longer has it.
/// </summary>
/// <remarks>
/// The handler used to ask storage whether the object exists and then fetch it: two calls,
/// and a window between them. It now fetches and answers the not-found the client throws,
/// the way the artifact and log handlers do. Both halves are pinned here against MinIO.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class SpecContentTests(SpaceCraftApi api)
{
    private const string Document = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.2.3
          description: Invoices and payments.
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

    [Fact]
    public async Task The_document_comes_back_as_uploaded()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);
        var specVersionId = await UploadAsync(client, organizationId, projectId);

        var read = await client.GetAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs/{specVersionId}/content", UriKind.Relative),
            TestContext.Current.CancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await read.Content.ReadFromJsonAsync<ContentPayload>(TestContext.Current.CancellationToken);
        payload!.Content.ShouldBe(Document);
        payload.Format.ShouldBe("yaml");
    }

    [Fact]
    public async Task A_row_whose_object_is_gone_answers_not_found_rather_than_a_server_error()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);
        var specVersionId = await UploadAsync(client, organizationId, projectId);

        // A bucket wiped between environments, reproduced one object at a time.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var storageKey = await db.SpecVersions
                .Where(s => s.Id == specVersionId)
                .Select(s => s.StorageKey)
                .SingleAsync(TestContext.Current.CancellationToken);

            await scope.ServiceProvider.GetRequiredService<IObjectStorage>()
                .DeleteAsync(storageKey, TestContext.Current.CancellationToken);
        }

        var read = await client.GetAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs/{specVersionId}/content", UriKind.Relative),
            TestContext.Current.CancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid organizationId, Guid projectId)
    {
        using var content = new MultipartFormDataContent();
        using var document = new StringContent(Document);
        content.Add(document, "file", "openapi.yaml");

        var uploaded = await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);

        uploaded.EnsureSuccessStatusCode();

        var payload = await uploaded.Content.ReadFromJsonAsync<UploadPayload>(TestContext.Current.CancellationToken);
        return payload!.SpecVersion.Id;
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId)> ProjectAsync(HttpClient client)
    {
        var organization = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        organization.EnsureSuccessStatusCode();
        var org = await organization.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        var project = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{org!.Id}/projects", UriKind.Relative),
            new { name = "Billing API" },
            TestContext.Current.CancellationToken);

        project.EnsureSuccessStatusCode();
        var created = await project.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        return (org.Id, created!.Id);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record SpecPayload(Guid Id);

    private sealed record UploadPayload(SpecPayload SpecVersion);

    private sealed record ContentPayload(string Content, string Format);
}
