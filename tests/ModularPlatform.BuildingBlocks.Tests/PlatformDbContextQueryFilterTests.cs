using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Entities;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PlatformDbContextQueryFilterTests
{
    [Fact]
    public async Task Query_filters_apply_tenant_and_soft_delete_together()
    {
        var currentTenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        var databaseName = $"query-filters-{Guid.CreateVersion7():N}";
        var options = CreateOptions(databaseName);

        await SeedAsync(options, currentTenantId, otherTenantId);

        await using var db = new TestDbContext(options, new TestTenantContext(currentTenantId, isSystem: false));

        var names = await db.Entities.Select(e => e.Name).ToListAsync();

        names.ShouldBe(["current-active"]);
    }

    [Fact]
    public async Task System_context_bypasses_tenant_filter_but_keeps_soft_delete_filter()
    {
        var currentTenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        var databaseName = $"query-filters-{Guid.CreateVersion7():N}";
        var options = CreateOptions(databaseName);

        await SeedAsync(options, currentTenantId, otherTenantId);

        await using var db = new TestDbContext(options, new TestTenantContext(null, isSystem: true));

        var names = await db.Entities
            .OrderBy(e => e.Name)
            .Select(e => e.Name)
            .ToListAsync();

        names.ShouldBe(["current-active", "other-active"]);
    }

    private static DbContextOptions<TestDbContext> CreateOptions(string databaseName)
    {
        return new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
    }

    private static async Task SeedAsync(DbContextOptions<TestDbContext> options, Guid currentTenantId, Guid otherTenantId)
    {
        await using var db = new TestDbContext(options, new TestTenantContext(null, isSystem: true));

        Add(db, new FilteredEntity { Name = "current-active" }, currentTenantId);
        Add(db, new FilteredEntity { Name = "current-deleted", DeletedAt = DateTimeOffset.UtcNow }, currentTenantId);
        Add(db, new FilteredEntity { Name = "other-active" }, otherTenantId);

        await db.SaveChangesAsync();
    }

    private static void Add(TestDbContext db, FilteredEntity entity, Guid tenantId)
    {
        db.Entities.Add(entity);
        db.Entry(entity).Property<Guid?>("TenantId").CurrentValue = tenantId;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid? tenantId, bool isSystem)
        {
            TenantId = tenantId;
            IsSystem = isSystem;
        }

        public Guid? UserId => IsSystem ? null : Guid.CreateVersion7();
        public Guid? TenantId { get; }
        public bool IsSystem { get; }
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
        : PlatformDbContext(options, tenant)
    {
        public override string ModuleName => "Test";
        public DbSet<FilteredEntity> Entities => Set<FilteredEntity>();
    }

    private sealed class FilteredEntity : Entity, ITenantScoped, ISoftDeletable
    {
        public string Name { get; init; } = string.Empty;
        public DateTimeOffset? DeletedAt { get; set; }
    }
}
