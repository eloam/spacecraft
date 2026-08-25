using SpaceCraft.Domain.Waitlist;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(120).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(Slug.MaxLength).IsRequired();

        builder.HasIndex(o => o.Slug).IsUnique();

        builder.HasMany(o => o.Members)
            .WithOne()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Projects)
            .WithOne()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Projects).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members");
        builder.HasKey(m => m.Id);

        // One membership row per user per organization.
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Email).HasMaxLength(320).IsRequired();
        builder.Property(w => w.ConfirmationTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(w => w.Source).HasMaxLength(64);
        builder.Property(w => w.IpHash).HasMaxLength(64);

        // Case-insensitive uniqueness: Email is lowercased on construction.
        builder.HasIndex(w => w.Email).IsUnique();
    }
}
