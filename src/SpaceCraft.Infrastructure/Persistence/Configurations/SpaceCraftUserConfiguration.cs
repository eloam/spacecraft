using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceCraft.Infrastructure.Identity;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

/// <summary>
/// What this application adds to Identity's own user mapping. The table, the key and the
/// Identity columns are configured by <c>IdentityDbContext</c>; only the columns declared on
/// <see cref="SpaceCraftUser"/> are described here.
/// </summary>
public sealed class SpaceCraftUserConfiguration : IEntityTypeConfiguration<SpaceCraftUser>
{
    public void Configure(EntityTypeBuilder<SpaceCraftUser> builder)
    {
        // The limit the register and profile validators enforce, stated on the column too, so
        // that a name the API refuses cannot reach the table by any other path.
        builder.Property(u => u.DisplayName).HasMaxLength(100);
    }
}
