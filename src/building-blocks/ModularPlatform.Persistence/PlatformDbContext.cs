using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Audit;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Persistence;

/// <summary>
/// Base DbContext for every module. Applies platform-wide conventions so entities stay boilerplate-free:
/// <list type="bullet">
/// <item>Postgres <c>xmin</c> optimistic concurrency token on every <see cref="Entity"/>.</item>
/// <item>shadow <c>TenantId</c> + global query filter on every <see cref="ITenantScoped"/> entity (defence-in-depth over RLS).</item>
/// <item>soft-delete filter on every <see cref="ISoftDeletable"/> entity.</item>
/// <item>per-module audit table <c>{module}_audit_entries</c>.</item>
/// <item>scans the module assembly for <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/>.</item>
/// </list>
/// </summary>
public abstract class PlatformDbContext(DbContextOptions options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>Stable module name; used to name the audit table and migrations history table.</summary>
    public abstract string ModuleName { get; }

    /// <summary>Referenced by tenant query filters; EF parameterises it per current tenant.</summary>
    protected Guid? CurrentTenantId => tenant.TenantId;

    /// <summary>True only for trusted system principals (worker/jobs/migration), which bypass the tenant filter.</summary>
    protected bool IsSystemContext => tenant.IsSystem;

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration($"{ModuleName.ToLowerInvariant()}_audit_entries"));
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;

            if (typeof(Entity).IsAssignableFrom(clr))
            {
                // Postgres xmin system column as the optimistic concurrency token (no stored RowVersion column).
                modelBuilder.Entity(clr)
                    .Property<uint>("xmin")
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }

            if (typeof(ITenantScoped).IsAssignableFrom(clr))
            {
                modelBuilder.Entity(clr).Property<Guid?>("TenantId");
                modelBuilder.Entity(clr).HasIndex("TenantId");
                ApplyFilterMethod("Tenant", nameof(BuildTenantFilter), clr, modelBuilder);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clr))
            {
                ApplyFilterMethod("SoftDelete", nameof(BuildSoftDeleteFilter), clr, modelBuilder);
            }
        }
    }

    private void ApplyFilterMethod(string filterKey, string builderName, Type clr, ModelBuilder modelBuilder)
    {
        var method = typeof(PlatformDbContext)
            .GetMethod(builderName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(clr);
        var filter = (LambdaExpression)method.Invoke(this, null)!;
        // EF Core 10 named query filters — tenant + soft-delete coexist without overwriting.
        modelBuilder.Entity(clr).HasQueryFilter(filterKey, filter);
    }

    private LambdaExpression BuildTenantFilter<TEntity>() where TEntity : class
    {
        // Only a real SYSTEM principal (worker/jobs/migration) bypasses the filter. An authenticated user whose
        // token lacks a tenant sees only their tenant's rows (null included) — NEVER everyone's. The old
        // `CurrentTenantId == null` short-circuit was a cross-tenant leak for any user with a missing claim.
        Expression<Func<TEntity, bool>> filter = e =>
            IsSystemContext || EF.Property<Guid?>(e, "TenantId") == CurrentTenantId;
        return filter;
    }

    private LambdaExpression BuildSoftDeleteFilter<TEntity>() where TEntity : class, ISoftDeletable
    {
        Expression<Func<TEntity, bool>> filter = e => e.DeletedAt == null;
        return filter;
    }
}
