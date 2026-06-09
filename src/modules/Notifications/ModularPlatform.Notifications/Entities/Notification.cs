using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Notifications.Entities;

/// <summary>
/// One in-app notification row (the per-user feed). Flat aggregate — references the user by Id, no
/// navigation. Tenant-scoped; audit + xmin concurrency applied by convention. One row is written per
/// SendNotification regardless of channels; <see cref="Channel"/> records which channel produced it.
/// Title/Body can hold rendered PII, so they are crypto-shredded under the recipient's DEK in the audit trail.
/// </summary>
internal sealed class Notification : AuditableEntity, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Channel { get; set; } = "inapp";
    [PersonalData]
    public string Title { get; set; } = string.Empty;
    [PersonalData]
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? ReadAt { get; set; }

    Guid IDataSubject.SubjectId => UserId;
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.TemplateKey).HasMaxLength(128).IsRequired();
        builder.Property(n => n.Channel).HasMaxLength(16).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(256).IsRequired();
        builder.Property(n => n.Body).IsRequired();
        builder.HasIndex(n => n.UserId);
    }
}
