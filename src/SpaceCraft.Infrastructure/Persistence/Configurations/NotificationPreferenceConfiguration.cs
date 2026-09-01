using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceCraft.Domain.Notifications;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_preferences");
        builder.HasKey(p => p.Id);

        // One answer per person, per kind, per channel. The uniqueness is the point: saving
        // the settings page twice must not leave two rows disagreeing about the same switch.
        builder.HasIndex(p => new { p.UserId, p.Kind, p.Channel }).IsUnique();
    }
}
