using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Notifications.Entities;

/// <summary>
/// Per-user channel opt-out. In-app feed rows remain mandatory; email/push delivery is optional.
/// </summary>
internal sealed class NotificationPreference : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Channel).HasMaxLength(16).IsRequired();
        builder.Property(p => p.Enabled).IsRequired();
        builder.HasIndex(p => new { p.UserId, p.Channel }).IsUnique();
    }
}
