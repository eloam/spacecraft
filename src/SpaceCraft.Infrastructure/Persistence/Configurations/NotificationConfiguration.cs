using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceCraft.Domain.Notifications;

namespace SpaceCraft.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Summary).HasMaxLength(500).IsRequired();
        builder.Property(n => n.Detail).HasMaxLength(2000);
        builder.Property(n => n.Link).HasMaxLength(400).IsRequired();
        builder.Property(n => n.ActionLabel).HasMaxLength(60).IsRequired();

        // The list one person opens: their own rows, newest first.
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });

        // And the count beside the bell, which is the query that runs most often in the whole
        // application — every open tab, every minute. Partial, so it is the size of what is
        // unread rather than of everything that person has ever been told: a reader who keeps
        // up has an index of nothing, and the count stays free however long the history gets.
        builder.HasIndex(n => n.UserId)
            .HasDatabaseName("ix_notifications_unread")
            .HasFilter("read_at IS NULL AND show_in_app");

        // The retention sweep: everything older than the cut-off, oldest first, a batch at a
        // time, across every user. Neither index above leads on created_at, so without this one
        // each batch is a scan of the table and a sort.
        builder.HasIndex(n => n.CreatedAt);

        builder.HasMany(n => n.Deliveries)
            .WithOne()
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(n => n.Deliveries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_deliveries");
        builder.HasKey(d => d.Id);

        // One delivery per notification per channel. Raising the same notification twice for
        // one channel would send it twice, which the uniqueness makes impossible rather than
        // unlikely.
        builder.HasIndex(d => new { d.NotificationId, d.Channel }).IsUnique();

        // The sweep a dispatcher runs: the whole table filtered down to a handful of rows.
        // Partial, so the index stays the size of the backlog rather than of the history.
        builder.HasIndex(d => new { d.Channel, d.CreatedAt })
            .HasDatabaseName("ix_notification_deliveries_pending")
            .HasFilter("sent_at IS NULL");
    }
}
