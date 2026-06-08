using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Persistence;

/// <summary>
/// Creates a no-tracking module DbContext pointed at the read replica (falls back to the write
/// connection when no replica is configured). No audit/concurrency interceptors — reads only.
/// Read-heavy modules scale the replica independently of the write primary.
/// </summary>
public interface IReadDbContextFactory<out TContext> where TContext : PlatformDbContext
{
    TContext Create();
}

internal sealed class ReadDbContextFactory<TContext>(string readConnectionString, ITenantContext tenant)
    : IReadDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    public TContext Create()
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(readConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options, tenant)!;
    }
}
