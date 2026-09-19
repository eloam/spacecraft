using SpaceCraft.Domain.Pipelines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class PipelineRunConfiguration : IEntityTypeConfiguration<PipelineRun>
{
    public void Configure(EntityTypeBuilder<PipelineRun> builder)
    {
        builder.ToTable("pipeline_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ResolvedVersion).HasMaxLength(64);
        builder.Property(r => r.ClaimedBy).HasMaxLength(128);
        builder.Property(r => r.FailureReason).HasMaxLength(2000);
        builder.Property(r => r.ArtifactStorageKey).HasMaxLength(512);

        // The target's settings as they were when this run was queued. Same lengths as the
        // columns they are copied from, so a value that fits on a target fits here.
        builder.Property(r => r.TargetPackageName).HasMaxLength(214).IsRequired();
        builder.Property(r => r.TargetRegistryUrl).HasMaxLength(512);
        builder.Property(r => r.TargetCredentialLabel).HasMaxLength(80);

        // Postgres's own row version as the concurrency token. Claiming is serialised by
        // FOR UPDATE SKIP LOCKED, but a claim is not the only write: the API cancels a run
        // while a worker holds it, from another process, and the worker's UPDATE used to
        // match on id alone — so a Cancel during Publish ended Succeeded, with a "Run
        // succeeded" notification for a run somebody had stopped. With xmin in the WHERE
        // clause the stale write matches no row and SaveChanges throws
        // DbUpdateConcurrencyException, which is where the executor finds out.
        //
        // xmin is a system column: Npgsql maps it, skips it in migrations, and never returns
        // it from SELECT *, so every FromSql on this table names it explicitly.
        // https://www.npgsql.org/efcore/modeling/concurrency.html
        builder.Property<uint>("xmin").IsRowVersion();

        // The queue read path: "oldest queued run first". Partial index keeps it tiny
        // regardless of how much run history accumulates.
        builder.HasIndex(r => new { r.Status, r.QueuedAt })
            .HasDatabaseName("ix_pipeline_runs_queue")
            .HasFilter("status = 1");

        // Reaper path: find Running runs whose lease has lapsed.
        builder.HasIndex(r => r.LeaseExpiresAt)
            .HasDatabaseName("ix_pipeline_runs_lease")
            .HasFilter("lease_expires_at IS NOT NULL");

        builder.HasIndex(r => new { r.ApiProjectId, r.QueuedAt });

        // The history the web polls every two seconds: the tenant's runs, newest first, a page
        // at a time. Leading on the organization and ending on the sort key lets Postgres walk
        // the index backwards and stop at the page size, rather than sort the tenant's whole
        // history on every poll. Everything else that filters on organization_id alone —
        // GetRun, CancelRun, the deleters — is served by the prefix.
        builder.HasIndex(r => new { r.OrganizationId, r.QueuedAt });

        // "Is a run in flight for this target?" — CreateRun, UpdateTarget and ProjectDeleter
        // all ask it, and SdkTargetId has no foreign key, so nothing indexes it by convention.
        // Status second, so the two in-flight values are found inside the target's runs
        // without reading the finished ones.
        builder.HasIndex(r => new { r.SdkTargetId, r.Status });

        // SpecDeleter's scope: the runs of a version being deleted. No foreign key either.
        // CreateRun's duplicate check also names the spec version, but it is served by
        // (sdk_target_id, status) above, which fixes two of its three columns.
        builder.HasIndex(r => r.SpecVersionId);

        builder.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.PipelineRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PipelineStepConfiguration : IEntityTypeConfiguration<PipelineStep>
{
    public void Configure(EntityTypeBuilder<PipelineStep> builder)
    {
        builder.ToTable("pipeline_steps");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.LogStorageKey).HasMaxLength(512);
        builder.Property(s => s.FailureReason).HasMaxLength(2000);

        builder.HasIndex(s => new { s.PipelineRunId, s.Ordinal }).IsUnique();
    }
}
