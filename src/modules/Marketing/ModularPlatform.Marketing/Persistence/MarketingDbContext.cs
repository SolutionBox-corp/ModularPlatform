using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Persistence;

/// <summary>
/// Marketing module's DbContext. Entity configs are discovered from this assembly; xmin concurrency, the per-user RLS
/// owner column, soft-delete filter and the per-module audit table are applied by the base.
/// </summary>
internal sealed class MarketingDbContext(DbContextOptions<MarketingDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "marketing";

    public DbSet<DataPull> DataPulls => Set<DataPull>();
    public DbSet<MetricSnapshot> MetricSnapshots => Set<MetricSnapshot>();
    public DbSet<MarketingAnalysis> MarketingAnalyses => Set<MarketingAnalysis>();
    public DbSet<VibeConversation> VibeConversations => Set<VibeConversation>();
    public DbSet<VibeMessage> VibeMessages => Set<VibeMessage>();
    public DbSet<MarketingTenantSnapshot> MarketingTenantSnapshots => Set<MarketingTenantSnapshot>();
}
