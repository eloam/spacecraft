using SpaceCraft.Domain.Waitlist;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Infrastructure.Persistence;

public sealed class SpaceCraftDbContext(DbContextOptions<SpaceCraftDbContext> options)
    : IdentityDbContext<SpaceCraftUser, SpaceCraftRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>
    /// The Data Protection key ring, shared by every process that touches a registry token.
    ///
    /// <para>
    /// In the database rather than on disk because the API encrypts those tokens and the
    /// runner decrypts them, from separate containers with no shared filesystem. A ring local
    /// to each meant the runner could not read what the API had written — which surfaced as
    /// "the key was not found in the key ring" in the middle of a publish, after a build had
    /// already succeeded.
    /// </para>
    ///
    /// <para>
    /// It also makes the ring survive a restart: an ephemeral one would make every stored
    /// token permanently unreadable each time a container was replaced.
    /// </para>
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<ApiProject> ApiProjects => Set<ApiProject>();
    public DbSet<SpecVersion> SpecVersions => Set<SpecVersion>();
    public DbSet<SdkTarget> SdkTargets => Set<SdkTarget>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<RegistryCredential> RegistryCredentials => Set<RegistryCredential>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("spacecraft");
        builder.ApplyConfigurationsFromAssembly(typeof(SpaceCraftDbContext).Assembly);

        // Identity tables land in the same schema but shed the "AspNet" prefix, which reads
        // oddly next to our own tables.
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table?.StartsWith("AspNet", StringComparison.Ordinal) == true)
            {
                entity.SetTableName(table["AspNet".Length..]);
            }
        }

        // Ids are UUIDv7 values assigned in the entity constructor, not database defaults.
        // Without this, EF sees a non-default key on a newly constructed entity, assumes the
        // row already exists, and issues an UPDATE that matches nothing — which surfaces as
        // a DbUpdateConcurrencyException the moment an entity is added through a navigation
        // rather than through DbSet.Add.
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (typeof(Domain.Common.Entity).IsAssignableFrom(entity.ClrType))
            {
                var id = entity.FindProperty(nameof(Domain.Common.Entity.Id));
                if (id is not null)
                {
                    id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                }
            }
        }

        // Applied last so it also covers the Identity tables and every configured index.
        SnakeCaseNaming.Apply(builder);
    }

    /// <summary>
    /// Stamps UpdatedAt centrally so no aggregate method can forget it.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Domain.Common.Entity>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.Touch();
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
