using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// What a delete takes with it, what it refuses, and what it leaves when it cannot finish.
/// </summary>
/// <remarks>
/// Runs, credentials, publications and invitations carry the ids of what owns them without a
/// foreign key, and the documents, artifacts and logs are objects in a bucket the database
/// knows nothing about. Whether a delete reaches all of them is a claim about Postgres and
/// MinIO together, so it is made against both, through the endpoints — the guard on the route
/// is part of it.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class DeletionTests(SpaceCraftApi api)
{
    private const string Spec = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.0.0
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

    /// <summary>
    /// The same contract, edited: a second version to retry the publish from, since a document
    /// uploaded twice byte for byte is refused as the version it already is.
    /// </summary>
    private const string RetriedSpec = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.0.0
          description: Invoices, payments and refunds.
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

    private const string PackageName = "billing-sdk";

    private const string Version = "1.0.0";

    [Fact]
    public async Task Deleting_an_organization_removes_what_no_foreign_key_covers()
    {
        var tenant = await SeedAsync();

        var deleted = await tenant.Owner.SendAsync(
            Delete($"/api/v1/organizations/{tenant.OrganizationId}", tenant.OrganizationName), Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.Organizations.AnyAsync(o => o.Id == tenant.OrganizationId, Ct)).ShouldBeFalse();

        // The four tables that name the organization without a constraint behind it.
        (await db.PipelineRuns.AnyAsync(r => r.OrganizationId == tenant.OrganizationId, Ct)).ShouldBeFalse();
        (await db.RegistryCredentials.AnyAsync(c => c.OrganizationId == tenant.OrganizationId, Ct)).ShouldBeFalse();
        (await db.Publications.AnyAsync(p => p.OrganizationId == tenant.OrganizationId, Ct)).ShouldBeFalse();
        (await db.OrganizationInvitations.AnyAsync(i => i.OrganizationId == tenant.OrganizationId, Ct)).ShouldBeFalse();

        await ShouldBeGoneAsync(tenant.SpecKey, tenant.ArtifactKey, tenant.LogKey);
    }

    [Fact]
    public async Task Deleting_a_project_removes_its_runs_publications_and_objects()
    {
        var tenant = await SeedAsync();

        var deleted = await tenant.Owner.SendAsync(
            Delete($"/api/v1/organizations/{tenant.OrganizationId}/projects/{tenant.ProjectId}", "Billing API"), Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.ApiProjects.AnyAsync(p => p.Id == tenant.ProjectId, Ct)).ShouldBeFalse();
        (await db.PipelineRuns.AnyAsync(r => r.ApiProjectId == tenant.ProjectId, Ct)).ShouldBeFalse();
        (await db.Publications.AnyAsync(p => p.SdkTargetId == tenant.TargetId, Ct)).ShouldBeFalse();

        // The credential is the organization's, not the project's, and stays.
        (await db.RegistryCredentials.AnyAsync(c => c.Id == tenant.CredentialId, Ct)).ShouldBeTrue();

        await ShouldBeGoneAsync(tenant.SpecKey, tenant.ArtifactKey, tenant.LogKey);
    }

    [Fact]
    public async Task Deleting_a_target_removes_its_runs_and_publications_and_keeps_the_document()
    {
        var tenant = await SeedAsync();

        var deleted = await tenant.Owner.SendAsync(
            Delete($"/api/v1/organizations/{tenant.OrganizationId}/projects/{tenant.ProjectId}/targets/{tenant.TargetId}", PackageName),
            Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.SdkTargets.AnyAsync(t => t.Id == tenant.TargetId, Ct)).ShouldBeFalse();
        (await db.PipelineRuns.AnyAsync(r => r.SdkTargetId == tenant.TargetId, Ct)).ShouldBeFalse();
        (await db.Publications.AnyAsync(p => p.SdkTargetId == tenant.TargetId, Ct)).ShouldBeFalse();

        // The spec was generated from, not owned by, the target: its row and document stay.
        (await db.SpecVersions.AnyAsync(s => s.Id == tenant.SpecId, Ct)).ShouldBeTrue();
        (await Storage().ExistsAsync(tenant.SpecKey, Ct)).ShouldBeTrue();

        await ShouldBeGoneAsync(tenant.ArtifactKey, tenant.LogKey);
    }

    [Fact]
    public async Task Deleting_a_spec_removes_the_runs_against_it_and_their_objects()
    {
        var tenant = await SeedAsync();

        var deleted = await tenant.Owner.DeleteAsync(SpecUrl(tenant), Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.SpecVersions.AnyAsync(s => s.Id == tenant.SpecId, Ct)).ShouldBeFalse();
        (await db.PipelineRuns.AnyAsync(r => r.SpecVersionId == tenant.SpecId, Ct)).ShouldBeFalse();

        // A failed attempt at publishing reached no registry, so its record goes with the run.
        (await db.Publications.AnyAsync(p => p.PipelineRunId == tenant.RunId, Ct)).ShouldBeFalse();

        await ShouldBeGoneAsync(tenant.SpecKey, tenant.ArtifactKey, tenant.LogKey);
    }

    [Fact]
    public async Task A_queued_run_refuses_every_delete_above_it()
    {
        var tenant = await SeedAsync();
        await QueueRunAsync(tenant);

        // A worker about to claim it would carry on against rows that no longer exist.
        foreach (var request in new[]
        {
            Delete($"/api/v1/organizations/{tenant.OrganizationId}", tenant.OrganizationName),
            Delete($"/api/v1/organizations/{tenant.OrganizationId}/projects/{tenant.ProjectId}", "Billing API"),
            Delete($"/api/v1/organizations/{tenant.OrganizationId}/projects/{tenant.ProjectId}/targets/{tenant.TargetId}", PackageName),
            new HttpRequestMessage(HttpMethod.Delete, SpecUrl(tenant)),
        })
        {
            using (request)
            {
                var refused = await tenant.Owner.SendAsync(request, Ct);

                refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, request.RequestUri!.ToString());
                (await refused.Content.ReadFromJsonAsync<ProblemPayload>(Ct))!.Detail.ShouldNotBeNull()
                    .ShouldContain("still queued or running");
            }
        }

        // And nothing went: the refusal came before the first row was touched.
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.Organizations.AnyAsync(o => o.Id == tenant.OrganizationId, Ct)).ShouldBeTrue();
        (await db.SpecVersions.AnyAsync(s => s.Id == tenant.SpecId, Ct)).ShouldBeTrue();
        (await Storage().ExistsAsync(tenant.SpecKey, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_spec_behind_a_published_package_cannot_be_deleted()
    {
        var tenant = await SeedAsync(published: true);

        var refused = await tenant.Owner.DeleteAsync(SpecUrl(tenant), Ct);

        // Registries are append-only: the package stays installable whatever is deleted here,
        // and the document it was generated from is the only record of where it came from.
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<ProblemPayload>(Ct))!.Detail.ShouldNotBeNull()
            .ShouldContain($"{PackageName}@{Version}");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.SpecVersions.AnyAsync(s => s.Id == tenant.SpecId, Ct)).ShouldBeTrue();
        (await Storage().ExistsAsync(tenant.SpecKey, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_admin_cannot_delete_the_organization()
    {
        var tenant = await SeedAsync();

        var adminEmail = Email();
        var admin = await api.SignedInAsync(adminEmail);
        await JoinAsync(tenant.OrganizationId, adminEmail, OrganizationRole.Admin);

        // Owner, not Admin: an admin was added by someone else, and this removes everything.
        var refused = await admin.SendAsync(
            Delete($"/api/v1/organizations/{tenant.OrganizationId}", tenant.OrganizationName), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.Organizations.AnyAsync(o => o.Id == tenant.OrganizationId, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_save_that_fails_leaves_the_document_where_its_row_still_points()
    {
        var client = await api.SignedInAsync(Email());
        var (organizationId, projectId) = await ProjectAsync(client);
        var specId = await UploadAsync(client, organizationId, projectId);
        var storageKey = await StorageKeyAsync(specId);

        // The database refuses the row, the way a lost connection or a constraint would, at
        // the one moment that matters: after the handler has decided and before the commit.
        await RefuseDeletionAsync();
        try
        {
            var refused = await client.DeleteAsync(SpecUrl(organizationId, projectId, specId), Ct);

            // 500 and nothing else: the handler had accepted the delete, and the save is what
            // failed. A 404 or a 400 would leave the row and the document just the same.
            refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await AllowDeletionAsync();
        }

        // Nothing happened: the row is there, and so is the document it points at. Removing
        // the object first used to leave a version that listed fine and could not be opened.
        (await ExistsAsync(db => db.SpecVersions.AnyAsync(s => s.Id == specId, Ct))).ShouldBeTrue();
        (await Storage().ExistsAsync(storageKey, Ct)).ShouldBeTrue();

        // And with the database willing again, both go.
        var deleted = await client.DeleteAsync(SpecUrl(organizationId, projectId, specId), Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ExistsAsync(db => db.SpecVersions.AnyAsync(s => s.Id == specId, Ct))).ShouldBeFalse();
        (await Storage().ExistsAsync(storageKey, Ct)).ShouldBeFalse();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private static Uri SpecUrl(Guid organizationId, Guid projectId, Guid specId) =>
        new($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs/{specId}", UriKind.Relative);

    private static Uri SpecUrl(Tenant tenant) => SpecUrl(tenant.OrganizationId, tenant.ProjectId, tenant.SpecId);

    /// <summary>
    /// A delete with the name typed back, which is what the organization, project and target
    /// deletes ask for.
    /// </summary>
    private static HttpRequestMessage Delete(string url, string name) =>
        new(HttpMethod.Delete, new Uri(url, UriKind.Relative))
        {
            Content = JsonContent.Create(new { name }),
        };

    private async Task ShouldBeGoneAsync(params string[] keys)
    {
        foreach (var key in keys)
        {
            (await Storage().ExistsAsync(key, Ct)).ShouldBeFalse(key);
        }
    }

    /// <summary>
    /// Everything a delete has to reach, in one tenant: an organization with a credential and
    /// a pending invitation, a project with a target and an uploaded document, and a finished
    /// run against them with its artifact and a step log in the bucket, and a record of the
    /// publish attempt.
    /// </summary>
    /// <param name="published">
    /// Whether that attempt reached the registry. A succeeded publication is what pins a
    /// spec; a failed one reached nothing and goes with its run. The succeeded one is seeded
    /// in the shape a retry leaves: one row per target and version, its PipelineRunId moved
    /// to a later run of the same version from a second document. The spec under test is
    /// then behind the package only by what was published from it, which is what the guard
    /// matches on — a guard joining runs by id would not find it, and this seed would let
    /// the delete through.
    /// </param>
    private async Task<Tenant> SeedAsync(bool published = false)
    {
        var ownerEmail = Email();
        var owner = await api.SignedInAsync(ownerEmail);
        var organizationName = $"Org{Guid.NewGuid():N}"[..14];
        var (organizationId, projectId) = await ProjectAsync(owner, organizationName);
        var specId = await UploadAsync(owner, organizationId, projectId);
        var specKey = await StorageKeyAsync(specId);
        var retriedSpecId = published ? await UploadAsync(owner, organizationId, projectId, RetriedSpec) : (Guid?)null;

        var credential = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/credentials", UriKind.Relative),
            new { registry = (int)RegistryKind.Npm, label = "npm automation", token = "npm_DeletionTestsTokenAaBbCcDdEeFfGgHh" },
            Ct);

        credential.EnsureSuccessStatusCode();

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

        var credentialId = await db.RegistryCredentials
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => c.Id)
            .SingleAsync(Ct);

        var ownerId = await db.Users
            .Where(u => u.Email == ownerEmail)
            .Select(u => u.Id)
            .SingleAsync(Ct);

        // Pending, with a live token behind it: the row that outlived its organization.
        db.OrganizationInvitations.Add(new OrganizationInvitation(
            organizationId, Email(), OrganizationRole.Member, ownerId,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow.AddDays(7)));

        var project = await db.ApiProjects
            .Include(p => p.Targets)
            .Include(p => p.SpecVersions)
            .SingleAsync(p => p.Id == projectId, Ct);

        var target = project.AddTarget(SdkLanguage.TypeScript, PackageName, RegistryKind.Npm);
        var spec = project.SpecVersions.Single(s => s.Id == specId);

        var run = new PipelineRun(
            organizationId, projectId, spec.Id, spec.Sequence, target,
            "https://registry.npmjs.org", "npm automation", RunTrigger.Manual, publishRequested: true);

        var artifactKey = StorageKeys.Artifact(organizationId, projectId, run.Id, "billing-sdk-1.0.0.tgz");
        var logKey = StorageKeys.StepLog(organizationId, projectId, run.Id, "validate");

        run.SetResolvedVersion(Version);
        run.SetArtifact(artifactKey);
        run.Step(StepKind.Validate).Succeed(logKey);
        run.Succeed();

        db.PipelineRuns.Add(run);

        var publishedBy = run;
        if (retriedSpecId is { } retriedId)
        {
            // The retry: the same version, resolved from the later document, and the row the
            // registry answered to.
            var retriedSpec = project.SpecVersions.Single(s => s.Id == retriedId);
            var retry = new PipelineRun(
                organizationId, projectId, retriedSpec.Id, retriedSpec.Sequence, target,
                "https://registry.npmjs.org", "npm automation", RunTrigger.Manual, publishRequested: true);

            retry.SetResolvedVersion(Version);
            retry.Succeed();

            db.PipelineRuns.Add(retry);
            publishedBy = retry;
        }

        var publication = new Publication(organizationId, publishedBy.Id, target.Id, RegistryKind.Npm, PackageName, Version);
        if (published)
        {
            publication.Succeed("https://www.npmjs.com/package/billing-sdk");
        }
        else
        {
            publication.Fail("The registry refused the token.");
        }

        db.Publications.Add(publication);
        await db.SaveChangesAsync(Ct);

        await storage.PutTextAsync(artifactKey, "not really a tarball", "application/gzip", Ct);
        await storage.PutTextAsync(logKey, "validated", "text/plain", Ct);

        return new Tenant(
            owner, organizationId, organizationName, projectId, target.Id, specId, run.Id, credentialId,
            specKey, artifactKey, logKey);
    }

    /// <summary>A second run against the same target and document, still waiting for a worker.</summary>
    private async Task QueueRunAsync(Tenant tenant)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var target = await db.SdkTargets.SingleAsync(t => t.Id == tenant.TargetId, Ct);
        var sequence = await db.SpecVersions
            .Where(s => s.Id == tenant.SpecId)
            .Select(s => s.Sequence)
            .SingleAsync(Ct);

        db.PipelineRuns.Add(new PipelineRun(
            tenant.OrganizationId, tenant.ProjectId, tenant.SpecId, sequence, target,
            "https://registry.npmjs.org", "npm automation", RunTrigger.Manual, publishRequested: false));

        await db.SaveChangesAsync(Ct);
    }

    private async Task JoinAsync(Guid organizationId, string email, OrganizationRole role)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync(Ct);

        var organization = await db.Organizations
            .Include(o => o.Members)
            .SingleAsync(o => o.Id == organizationId, Ct);

        organization.AddMember(userId, role);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// A trigger that refuses to delete any spec row while it stands. Every test in the
    /// collection runs one after another against this database, so it stops nobody else's.
    /// </summary>
    private async Task RefuseDeletionAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION spacecraft.refuse_deletion() RETURNS trigger
            LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Refused by DeletionTests.'; END $$;
            """,
            Ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER refuse_deletion BEFORE DELETE ON spacecraft.spec_versions
            FOR EACH ROW EXECUTE FUNCTION spacecraft.refuse_deletion();
            """,
            Ct);
    }

    private async Task AllowDeletionAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS refuse_deletion ON spacecraft.spec_versions", Ct);
    }

    private async Task<bool> ExistsAsync(Func<SpaceCraftDbContext, Task<bool>> query)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>());
    }

    private IObjectStorage Storage() => api.Services.GetRequiredService<IObjectStorage>();

    private async Task<string> StorageKeyAsync(Guid specId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.SpecVersions
            .Where(s => s.Id == specId)
            .Select(s => s.StorageKey)
            .SingleAsync(Ct);
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, Guid organizationId, Guid projectId, string spec = Spec)
    {
        using var content = new MultipartFormDataContent();
        using var document = new StringContent(spec);
        content.Add(document, "file", "openapi.yaml");

        var uploaded = await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            Ct);

        uploaded.EnsureSuccessStatusCode();

        return (await uploaded.Content.ReadFromJsonAsync<UploadPayload>(Ct))!.SpecVersion.Id;
    }

    private static async Task<(Guid OrganizationId, Guid ProjectId)> ProjectAsync(
        HttpClient client, string? organizationName = null)
    {
        var organization = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = organizationName ?? $"Org{Guid.NewGuid():N}"[..14] },
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

    private sealed record Tenant(
        HttpClient Owner,
        Guid OrganizationId,
        string OrganizationName,
        Guid ProjectId,
        Guid TargetId,
        Guid SpecId,
        Guid RunId,
        Guid CredentialId,
        string SpecKey,
        string ArtifactKey,
        string LogKey);

    private sealed record IdPayload(Guid Id);

    private sealed record UploadPayload(IdPayload SpecVersion);

    private sealed record ProblemPayload(string? Detail);
}
