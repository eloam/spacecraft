using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Pipeline;
using SpaceCraft.Pipeline.Execution;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// The notification a finished run raises.
/// </summary>
/// <remarks>
/// This one is raised in the runner, a separate process, and reaches the API through the
/// database. That makes it the hardest of the three to cover and the easiest to leave to a
/// manual check — which is what it had been. The pipeline is driven here directly, against
/// the same database the API reads, which is exactly the arrangement the two processes have
/// in production.
/// <para>
/// The run fails at its first step, and deliberately: a four-part version is ordinary for
/// NuGet and inexpressible in npm, so validation refuses it without any of the later steps
/// having to exist. What is under test is who is told and what the message says, not what
/// npm does.
/// </para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class RunNotificationTests(SpaceCraftApi api)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);

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
    public async Task A_finished_run_tells_whoever_started_it()
    {
        var owner = await api.SignedInAsync(Email());
        var (organizationId, runId) = await QueuedRunAsync(owner);

        await ExecuteAsync(runId);

        var feed = await FeedAsync(owner);
        var notification = feed.Items.ShouldHaveSingleItem();

        notification.Kind.ShouldBe("RunCompleted");
        notification.Title.ShouldStartWith("Run failed");
        notification.Link.ShouldBe($"/orgs/{organizationId}/runs/{runId}");

        // The result and the particulars, which is the whole reason to be told at all.
        notification.Detail.ShouldNotBeNull().ShouldContain("Validate step failed");
    }

    [Fact]
    public async Task A_run_nobody_started_tells_the_people_who_answer_for_the_organization()
    {
        var owner = await api.SignedInAsync(Email());
        var (organizationId, runId) = await QueuedRunAsync(owner);

        var adminEmail = Email();
        var admin = await api.SignedInAsync(adminEmail);
        await JoinAsync(organizationId, adminEmail, OrganizationRole.Admin);

        // What an automatic run looks like: queued by the system, with nobody to tell.
        // Silence would make the runs nobody is watching the ones nobody hears about.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            await db.PipelineRuns
                .Where(r => r.Id == runId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(r => r.TriggeredByUserId, (Guid?)null),
                    TestContext.Current.CancellationToken);
        }

        await ExecuteAsync(runId);

        var forAdmin = await FeedAsync(admin);
        forAdmin.Items.ShouldContain(n => n.Kind == "RunCompleted");
    }

    /// <summary>
    /// Drives the run the way the runner does: the real executor, the real validator, the
    /// real publisher, against the API's own database.
    /// </summary>
    private async Task ExecuteAsync(Guid runId)
    {
        using var scope = api.Services.CreateScope();
        var services = scope.ServiceProvider;

        // No generators, builders or publishers: the run never reaches them. Registering
        // stubs would only invite the test to drift away from the step it is about.
        var executor = new PipelineExecutor(
            services.GetRequiredService<SpaceCraftDbContext>(),
            services.GetRequiredService<IObjectStorage>(),
            services.GetRequiredService<ISpecValidator>(),
            [],
            [],
            [],
            [],
            services.GetRequiredService<ISecretProtector>(),
            services.GetRequiredService<INotificationPublisher>(),
            Options.Create(new PipelineOptions()),
            NullLogger<PipelineExecutor>.Instance);

        await executor.ExecuteAsync(runId, TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid OrganizationId, Guid RunId)> QueuedRunAsync(HttpClient client)
    {
        var (organizationId, projectId) = await ProjectAsync(client);

        using var content = new MultipartFormDataContent();
        using var document = new StringContent(FourPartVersion);
        content.Add(document, "file", "openapi.yaml");

        var uploaded = await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);

        uploaded.EnsureSuccessStatusCode();

        var spec = System.Text.Json.JsonSerializer.Deserialize<UploadPayload>(
            await uploaded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), Json)!;

        var target = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/targets", UriKind.Relative),
            new { language = 1, packageName = "billing-sdk", autoPublish = false },
            TestContext.Current.CancellationToken);

        target.EnsureSuccessStatusCode();
        var created = await target.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        var run = await client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/runs", UriKind.Relative),
            new { specVersionId = spec.SpecVersion.Id, sdkTargetId = created!.Id, publish = false },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();
        var queued = await run.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken);

        return (organizationId, queued!.Id);
    }

    private async Task JoinAsync(Guid organizationId, string email, OrganizationRole role)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

        organization.AddMember(userId, role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Feed> FeedAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/v1/notifications", UriKind.Relative), TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Feed>(TestContext.Current.CancellationToken))!;
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

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

    private sealed record Feed(int UnreadCount, List<Item> Items);

    private sealed record Item(string Kind, string Title, string? Detail, string Link);
}
