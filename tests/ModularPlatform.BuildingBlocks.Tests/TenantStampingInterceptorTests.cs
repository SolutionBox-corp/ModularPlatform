using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Entities;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class TenantStampingInterceptorTests
{
    [Fact]
    public async Task SavingChanges_does_not_overwrite_explicit_tenant_id()
    {
        var currentTenantId = Guid.CreateVersion7();
        var explicitTenantId = Guid.CreateVersion7();
        var tenant = new TestTenantContext(currentTenantId);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"tenant-stamping-{Guid.CreateVersion7():N}")
            .AddInterceptors(new TenantStampingInterceptor(tenant))
            .Options;
        await using var db = new TestDbContext(options, tenant);
        var entity = new TenantScopedEntity();

        db.Entities.Add(entity);
        db.Entry(entity).Property<Guid?>("TenantId").CurrentValue = explicitTenantId;

        await db.SaveChangesAsync();

        db.Entry(entity).Property<Guid?>("TenantId").CurrentValue.ShouldBe(explicitTenantId);
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? UserId => Guid.CreateVersion7();
        public Guid? TenantId { get; } = tenantId;
        public bool IsSystem => false;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
        : PlatformDbContext(options, tenant)
    {
        public override string ModuleName => "Test";
        public DbSet<TenantScopedEntity> Entities => Set<TenantScopedEntity>();
    }

    private sealed class TenantScopedEntity : Entity, ITenantScoped;
}
