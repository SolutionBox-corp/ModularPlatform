using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// One pull of data from a marketing source (GA4, GSC, PostHog, Reddit, Trends). <see cref="IUserOwned"/> → RLS-isolated
/// so a user only ever sees their own pulls. Created <see cref="PullStatus.Pending"/> by the accepting request, advanced
/// to a terminal state by the durable worker. The raw provider payload is kept verbatim in <see cref="RawResultJson"/>
/// (auditability + re-analysis); normalized numbers are projected into <see cref="MetricSnapshot"/> rows.
/// </summary>
internal sealed class DataPull : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public PullSource Source { get; set; }
    public PullStatus Status { get; set; }

    /// <summary>The pull request parameters (date range, property id, query, …) as JSON.</summary>
    public string? ParamsJson { get; set; }

    /// <summary>The verbatim provider response (jsonb). Null until the worker completes the pull.</summary>
    public string? RawResultJson { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class DataPullConfiguration : IEntityTypeConfiguration<DataPull>
{
    public void Configure(EntityTypeBuilder<DataPull> builder)
    {
        builder.ToTable("data_pulls");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.RawResultJson).HasColumnType("jsonb");
        builder.Property(p => p.ParamsJson).HasColumnType("jsonb");
        builder.Property(p => p.ErrorCode).HasMaxLength(128);
        builder.HasIndex(p => new { p.UserId, p.Source });
    }
}
