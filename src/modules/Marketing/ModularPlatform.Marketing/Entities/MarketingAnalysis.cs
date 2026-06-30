using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// An AI (Claude) analysis of one or more pulls — the user-facing "insight". <see cref="IUserOwned"/> → RLS-isolated.
/// Produced by the durable worker after a pull completes; <see cref="DataPullId"/> links back to the trigger pull.
/// </summary>
internal sealed class MarketingAnalysis : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid? DataPullId { get; set; }
    public PullSource Source { get; set; }

    /// <summary>Short headline summary of the analysis.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Structured insights + recommended actions as JSON (rendered in the UI).</summary>
    public string? InsightsJson { get; set; }

    public DateTimeOffset AnalyzedAt { get; set; }
}

internal sealed class MarketingAnalysisConfiguration : IEntityTypeConfiguration<MarketingAnalysis>
{
    public void Configure(EntityTypeBuilder<MarketingAnalysis> builder)
    {
        builder.ToTable("marketing_analyses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(2048).IsRequired();
        builder.Property(a => a.InsightsJson).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.UserId, a.AnalyzedAt, a.Id })
            .IsDescending(false, true, true);
    }
}
