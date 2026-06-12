using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Notifications.Entities;

/// <summary>
/// One in-app notification row (the per-user feed). Flat aggregate — references the user by Id, no
/// navigation. Tenant-scoped; audit + xmin concurrency applied by convention. One row is written per
/// SendNotification regardless of channels; <see cref="Channel"/> records which channel produced it.
/// Title/Body hold rendered PII, so they are <see cref="EncryptedAttribute">encrypted AT REST</see> under the
/// recipient's DEK (a penc:v2 envelope in the live column) AND crypto-shredded in the audit trail — GDPR erasure
/// (shredding the DEK) makes both unreadable even if the per-module anonymizer never runs.
/// </summary>
internal sealed class Notification : AuditableEntity, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Channel { get; set; } = "inapp";
    [PersonalData]
    [Encrypted]
    public string Title { get; set; } = string.Empty;
    [PersonalData]
    [Encrypted]
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Optional dedup key — a UNIQUE partial index makes a keyed send exactly-once (e.g. one welcome per user).</summary>
    public string? IdempotencyKey { get; set; }

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
        // [Encrypted] columns store a penc:v2 envelope, not the plaintext — size for the envelope (mirrors users.Email).
        builder.Property(n => n.Title).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.Body).IsRequired();
        builder.Property(n => n.IdempotencyKey).HasMaxLength(128);
        // Feed query is filter-by-user + sort-by-recency: a composite index avoids a sort-after-filter at scale.
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        // Exactly-once for keyed sends; partial so the common (null-key) notifications are unconstrained.
        builder.HasIndex(n => n.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
