using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Rls;

namespace ModularPlatform.Persistence;

/// <summary>
/// Creates a no-tracking module DbContext pointed at the read replica (falls back to the write
/// connection when no replica is configured). No audit/concurrency interceptors — reads only.
/// Read-heavy modules scale the replica independently of the write primary.
/// When RLS is enabled the connection is the least-privilege runtime role and the principal-session
/// interceptor stamps the GUCs the policies read, so reads are tenant/principal-isolated at the DB too.
/// </summary>
public interface IReadDbContextFactory<out TContext> where TContext : PlatformDbContext
{
    TContext Create();
}

internal sealed class ReadDbContextFactory<TContext>(string readConnectionString, ITenantContext tenant, bool rlsEnabled)
    : IReadDbContextFactory<TContext>
    where TContext : PlatformDbContext
{
    public TContext Create()
    {
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(readConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        if (rlsEnabled)
        {
            builder.AddInterceptors(new PrincipalSessionConnectionInterceptor(tenant));
        }

        return (TContext)Activator.CreateInstance(typeof(TContext), builder.Options, tenant)!;
    }
}
