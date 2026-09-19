using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("organization_invitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(i => i.TokenHash).IsUnique();

        // The listing an admin reads, and the supersession that puts an older invitation out
        // when a newer one is sent to the same address.
        builder.HasIndex(i => new { i.OrganizationId, i.Email });
    }
}
