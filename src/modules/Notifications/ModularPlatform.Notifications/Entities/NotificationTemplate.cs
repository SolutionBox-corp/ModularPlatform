using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Notifications.Entities;

/// <summary>
/// A reusable message template, keyed by <see cref="Key"/> (+ locale). Subject/Body carry
/// {placeholders} substituted from the SendNotification data dictionary at render time.
/// Not tenant-scoped — templates are platform-shared content.
/// </summary>
internal sealed class NotificationTemplate : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Key).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Locale).HasMaxLength(8).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Body).IsRequired();
        builder.HasIndex(t => new { t.Key, t.Locale }).IsUnique();
    }
}
