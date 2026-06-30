using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// A single normalized metric data point projected from a <see cref="DataPull"/> (e.g. gsc:clicks for a query, ga4:users
/// for a channel). <see cref="IUserOwned"/> → RLS-isolated. High-volume + append-only, so it derives <see cref="Entity"/>
/// (no audit trail). <see cref="RecordedAt"/> is the date the metric REFERS to (its time bucket), not the insert time.
/// </summary>
internal sealed class MetricSnapshot : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid DataPullId { get; set; }
    public PullSource Source { get; set; }

    /// <summary>Dotted metric key, e.g. <c>gsc:clicks</c> or <c>ga4:sessions</c>.</summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>The dimension this value belongs to (a query string, channel, subreddit, …), if any.</summary>
    public string? Dimension { get; set; }

    public double Value { get; set; }

    /// <summary>Extra per-point data (impressions, ctr, position, …) as JSON.</summary>
    public string? DetailJson { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}

internal sealed class MetricSnapshotConfiguration : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> builder)
    {
        builder.ToTable("metric_snapshots");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.MetricName).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Dimension).HasMaxLength(512);
        builder.Property(m => m.DetailJson).HasColumnType("jsonb");
        builder.HasIndex(m => new { m.UserId, m.Source, m.RecordedAt, m.Id })
            .IsDescending(false, false, true, true);
        builder.HasIndex(m => new { m.UserId, m.RecordedAt, m.Id })
            .IsDescending(false, true, true);
    }
}
