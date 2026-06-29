using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Audit;
using ModularPlatform.Persistence.Entities;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class AuditInterceptorTests
{
    [Fact]
    public async Task Auditable_entities_are_stamped_on_create_and_update_from_the_current_context()
    {
        var tenant = new TestTenantContext();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"audit-stamps-{Guid.CreateVersion7():N}")
            .AddInterceptors(new AuditInterceptor(
                clock,
                tenant,
                auditOptions: Options.Create(new AuditOptions { IpStorage = AuditIpStorageMode.Full })))
            .Options;

        await using var createDb = new TestDbContext(options, tenant);
        var entity = new AuditedEntity { Name = "created" };
        createDb.Entities.Add(entity);

        await createDb.SaveChangesAsync();

        entity.CreatedAt.ShouldBe(clock.UtcNow);
        entity.CreatedBy.ShouldBe(tenant.UserId);
        entity.UpdatedAt.ShouldBeNull();
        entity.UpdatedBy.ShouldBeNull();

        clock.UtcNow = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
        await using var updateDb = new TestDbContext(options, tenant);
        var saved = await updateDb.Entities.SingleAsync(e => e.Id == entity.Id);
        saved.Name = "updated";

        await updateDb.SaveChangesAsync();

        saved.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        saved.CreatedBy.ShouldBe(tenant.UserId);
        saved.UpdatedAt.ShouldBe(clock.UtcNow);
        saved.UpdatedBy.ShouldBe(tenant.UserId);
    }

    [Fact]
    public async Task Update_audit_row_records_changed_columns_only()
    {
        var tenant = new TestTenantContext();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"audit-changed-only-{Guid.CreateVersion7():N}")
            .AddInterceptors(new AuditInterceptor(
                clock,
                tenant,
                auditOptions: Options.Create(new AuditOptions { IpStorage = AuditIpStorageMode.Full })))
            .Options;

        await using var createDb = new TestDbContext(options, tenant);
        var entity = new AuditedEntity { Name = "before", Description = "must-not-be-repeated" };
        createDb.Entities.Add(entity);
        await createDb.SaveChangesAsync();

        clock.UtcNow = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
        await using var updateDb = new TestDbContext(options, tenant);
        var saved = await updateDb.Entities.SingleAsync(e => e.Id == entity.Id);
        saved.Name = "after";

        await updateDb.SaveChangesAsync();

        var updateAudit = await updateDb.AuditEntries.SingleAsync(a =>
            a.EntityType == nameof(AuditedEntity) && a.EntityId == entity.Id.ToString() && a.Action == "Update");

        var changedColumns = JsonSerializer.Deserialize<string[]>(updateAudit.ChangedColumns)!;
        changedColumns.ShouldContain(nameof(AuditedEntity.Name));
        changedColumns.ShouldNotContain(nameof(AuditedEntity.Description));

        using var values = JsonDocument.Parse(updateAudit.NewValues);
        values.RootElement.TryGetProperty(nameof(AuditedEntity.Name), out var name).ShouldBeTrue();
        name.GetString().ShouldBe("after");
        values.RootElement.TryGetProperty(nameof(AuditedEntity.Description), out _).ShouldBeFalse();
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
        : PlatformDbContext(options, tenant)
    {
        public override string ModuleName => "Test";
        public DbSet<AuditedEntity> Entities => Set<AuditedEntity>();
    }

    private sealed class AuditedEntity : AuditableEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public Guid? TenantId { get; } = Guid.CreateVersion7();
        public bool IsSystem => false;
        public string? IpAddress => "127.0.0.1";
    }
}
