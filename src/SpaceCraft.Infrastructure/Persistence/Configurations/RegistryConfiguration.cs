using SpaceCraft.Domain.Registries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class RegistryCredentialConfiguration : IEntityTypeConfiguration<RegistryCredential>
{
    public void Configure(EntityTypeBuilder<RegistryCredential> builder)
    {
        builder.ToTable("registry_credentials");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Label).HasMaxLength(120).IsRequired();
        builder.Property(c => c.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(c => c.RegistryUrl).HasMaxLength(512).IsRequired();

        // Encrypted bytes. There is deliberately no plaintext column anywhere in the schema.
        builder.Property(c => c.CipherText).HasColumnType("bytea").IsRequired();

        builder.HasIndex(c => new { c.OrganizationId, c.Registry });
    }
}

public sealed class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> builder)
    {
        builder.ToTable("publications");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PackageName).HasMaxLength(214).IsRequired();
        builder.Property(p => p.Version).HasMaxLength(64).IsRequired();
        builder.Property(p => p.RegistryUrl).HasMaxLength(1000);
        builder.Property(p => p.FailureReason).HasMaxLength(2000);

        // Registries are append-only: a given target/version pair can only be published once.
        // This constraint is what makes re-running a pipeline safe.
        builder.HasIndex(p => new { p.SdkTargetId, p.Version })
            .IsUnique()
            .HasDatabaseName("ux_publications_target_version");

        builder.HasIndex(p => p.PipelineRunId);
        builder.HasIndex(p => p.OrganizationId);
    }
}
