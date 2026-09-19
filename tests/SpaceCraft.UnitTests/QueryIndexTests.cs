using Microsoft.EntityFrameworkCore;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Every query the API runs on a table without a foreign key to lead it, or sorts a tenant's
/// whole history for a page, has an index shaped for it.
/// </summary>
/// <remarks>
/// Written against the model rather than a database, since which index exists is a decision
/// taken in the configuration and the migration only restates it. Each fact names the query
/// the index serves, so that removing the index means arguing with the query first.
/// </remarks>
public sealed class QueryIndexTests
{
    [Fact]
    public void ListRuns_pages_the_tenant_history_off_an_index_ending_on_the_sort_key() =>
        // WHERE organization_id = ? ORDER BY queued_at DESC LIMIT ?, polled every two seconds.
        IndexColumns<PipelineRun>().ShouldContain("OrganizationId, QueuedAt");

    [Fact]
    public void Runs_in_flight_for_a_target_are_found_without_reading_its_finished_ones() =>
        // CreateRun, UpdateTarget and ProjectDeleter: WHERE sdk_target_id = ? AND status IN (...).
        IndexColumns<PipelineRun>().ShouldContain("SdkTargetId, Status");

    [Fact]
    public void Runs_of_a_spec_version_are_indexed_although_nothing_declares_the_key() =>
        // SpecDeleter's scope: WHERE spec_version_id = ?. CreateRun's duplicate check names the
        // version too, but (sdk_target_id, status) fixes two of its columns and serves it.
        IndexColumns<PipelineRun>().ShouldContain("SpecVersionId");

    [Fact]
    public void ListPublications_pages_the_tenant_history_off_an_index_ending_on_the_sort_key() =>
        // WHERE organization_id = ? ORDER BY created_at DESC LIMIT ?.
        IndexColumns<Publication>().ShouldContain("OrganizationId, CreatedAt");

    [Fact]
    public void The_retention_sweep_walks_notifications_oldest_first_off_an_index() =>
        // WHERE created_at < ? ORDER BY created_at LIMIT ?, across every user.
        IndexColumns<Notification>().ShouldContain("CreatedAt");

    [Fact]
    public void The_single_column_organization_indexes_are_gone()
    {
        // Their prefix on the composite ones serves the same lookups; keeping both would be a
        // second write per insert for no read.
        IndexColumns<PipelineRun>().ShouldNotContain("OrganizationId");
        IndexColumns<Publication>().ShouldNotContain("OrganizationId");
    }

    /// <summary>Each index as its column list, in order — the order is the point.</summary>
    private static List<string> IndexColumns<TEntity>()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

        return context.Model.FindEntityType(typeof(TEntity))!
            .GetIndexes()
            .Select(index => string.Join(", ", index.Properties.Select(p => p.Name)))
            .ToList();
    }
}
