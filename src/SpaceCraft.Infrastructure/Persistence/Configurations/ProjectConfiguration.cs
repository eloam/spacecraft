using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class ApiProjectConfiguration : IEntityTypeConfiguration<ApiProject>
{
    public void Configure(EntityTypeBuilder<ApiProject> builder)
    {
        builder.ToTable("api_projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(p => p.GitHubRepository).HasMaxLength(200);
        builder.Property(p => p.GitHubSpecPath).HasMaxLength(400);
        builder.Property(p => p.GitHubBranch).HasMaxLength(200);

        // Slugs are unique per organization, not globally — two tenants may both have "billing".
        builder.HasIndex(p => new { p.OrganizationId, p.Slug }).IsUnique();

        // Webhook lookup path: find the project watching this repo.
        builder.HasIndex(p => p.GitHubRepository)
            .HasFilter("github_repository IS NOT NULL");

        builder.HasMany(p => p.SpecVersions)
            .WithOne()
            .HasForeignKey(s => s.ApiProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Targets)
            .WithOne()
            .HasForeignKey(t => t.ApiProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.SpecVersions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SpecVersionConfiguration : IEntityTypeConfiguration<SpecVersion>
{
    public void Configure(EntityTypeBuilder<SpecVersion> builder)
    {
        builder.ToTable("spec_versions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(s => s.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.ApiTitle).HasMaxLength(200);
        builder.Property(s => s.ApiVersion).HasMaxLength(64);
        builder.Property(s => s.GitCommitSha).HasMaxLength(40);

        // Diagnostics are a JSON array of {severity, pointer, message}.
        builder.Property(s => s.Diagnostics).HasColumnType("jsonb");

        builder.HasIndex(s => new { s.ApiProjectId, s.Sequence }).IsUnique();

        // Lets an upload short-circuit when the bytes are identical to a previous version.
        builder.HasIndex(s => new { s.ApiProjectId, s.ContentHash });
    }
}

public sealed class SdkTargetConfiguration : IEntityTypeConfiguration<SdkTarget>
{
    public void Configure(EntityTypeBuilder<SdkTarget> builder)
    {
        builder.ToTable("sdk_targets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.PackageName).HasMaxLength(214).IsRequired();
        builder.Property(t => t.LastPublishedVersion).HasMaxLength(64);

        // One target per language per project.
        // No foreign key: a credential can be revoked and deleted while targets still name
        // it, and the publish step reports that clearly. A cascade would silently reset them
        // to "whichever is usable", which is the ambiguity this column exists to remove.
        builder.Property(t => t.RegistryCredentialId);

        builder.HasIndex(t => new { t.ApiProjectId, t.Language }).IsUnique();

        // Deliberately not unique across the instance any more.
        //
        // It read "two projects must not fight over the same package name on the same
        // registry", which is true of two projects in one organization and false between
        // tenants: the endpoint lives on the credential, so one organization publishing
        // billing-sdk to its own Nexus was refused because an unrelated one had taken that
        // name on npmjs. It was also byte-exact, which only npm's lowercase rule makes safe —
        // NuGet ids are case-insensitive and PyPI names are normalised under PEP 503, so
        // Acme.Sdk and acme.sdk inserted as two rows for one real package.
        //
        // The registry is the authority on who owns a name, and it says so at publish time.
        // What this platform can usefully catch is an organization colliding with itself,
        // which ProjectEndpoints checks within the organization before the row is written.
        builder.HasIndex(t => new { t.Registry, t.PackageName });
    }
}
