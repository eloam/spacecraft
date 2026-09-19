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
/// What a delete takes with it, and what it leaves when it cannot finish.
/// </summary>
/// <remarks>
/// Runs, credentials and publications carry the ids of what owns them without a foreign key,
/// and the documents, artifacts and logs are objects in a bucket the database knows nothing
/// about. Whether a delete reaches all of them is a claim about Postgres and MinIO together,
/// so it is made against both.
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

    private static async Task<Guid> UploadAsync(HttpClient client, Guid organizationId, Guid projectId)
    {
        using var content = new MultipartFormDataContent();
        using var document = new StringContent(Spec);
        content.Add(document, "file", "openapi.yaml");

        var uploaded = await client.PostAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects/{projectId}/specs", UriKind.Relative),
            content,
            Ct);

        uploaded.EnsureSuccessStatusCode();

        return (await uploaded.Content.ReadFromJsonAsync<UploadPayload>(Ct))!.SpecVersion.Id;
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

    private sealed record UploadPayload(IdPayload SpecVersion);
}
