using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// A run whose version the registry cannot take is queued, not refused.
/// </summary>
/// <remarks>
/// The document is valid and another registry would take this version, so the refusal belongs
/// to the pairing rather than to either half — and the pipeline's first step is validation,
/// which exists to judge the document at the moment of use. Refusing at creation was quieter
/// in the wrong way: no run, no step, no log, and nothing in the history for someone waiting
/// on a publish to find.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class RunRefusalTests(SpaceCraftApi api)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    // Otherwise complete, because projects validate strictly by default: the document has to
    // be one the policy is happy with so that the version is the only thing left to refuse.
    // Ordinary for NuGet, inexpressible in npm — which is the whole point.
    private const string FourPartVersion = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.2.3.4
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
    public async Task A_version_the_target_registry_cannot_take_still_queues_a_run()
    {
        var client = await api.SignedInAsync($"{Guid.NewGuid():N}@example.test");
        var (organizationId, projectId) = await ProjectAsync(client);

        var (spec, raw) = await UploadAsync(client, organizationId, projectId);

        // Valid with warnings, not invalid: the document is otherwise fine, and three of the
        // five registries would take this version. If this ever fails, the body says why.
        spec.SpecVersion.ValidationStatus.ShouldBe(3, raw);

        var target = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/targets", UriKind.Relative),
            new { language = 1, packageName = "billing-sdk", autoPublish = false },
            TestContext.Current.CancellationToken);

        target.EnsureSuccessStatusCode();
        var created = await target.Content.ReadFromJsonAsync<TargetPayload>(TestContext.Current.CancellationToken);

        var run = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/runs", UriKind.Relative),
            new { specVersionId = spec.SpecVersion.Id, sdkTargetId = created!.Id, publish = false },
            TestContext.Current.CancellationToken);

        // Accepted: the version is the pipeline's to judge, and a run that fails at validate
        // leaves a record where a refused request leaves nothing.
        run.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        (await db.PipelineRuns.CountAsync(r => r.ApiProjectId == projectId, TestContext.Current.CancellationToken))
            .ShouldBe(1);

        // And the version this run would publish is still refused, which is what its first
        // step will report. Asserted here because no worker runs in these tests.
        RegistryVersion.Render(RegistryKind.Npm, "1.2.3.4").Refusal
            .ShouldNotBeNull()
            .ShouldContain("npm takes three");
    }

    private static async Task<(UploadPayload Payload, string Raw)> UploadAsync(
        HttpClient client, Guid organizationId, Guid projectId)
    {
        using var content = new MultipartFormDataContent();
        using var document = new StringContent(FourPartVersion);
        content.Add(document, "file", "openapi.yaml");

        var uploaded = await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);

        uploaded.EnsureSuccessStatusCode();

        var raw = await uploaded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (System.Text.Json.JsonSerializer.Deserialize<UploadPayload>(raw, Json)!, raw);
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

    private sealed record TargetPayload(Guid Id);

    private sealed record SpecPayload(Guid Id, int ValidationStatus);

    private sealed record UploadPayload(SpecPayload SpecVersion);

    private sealed record ProblemPayload(string? Detail);
}
