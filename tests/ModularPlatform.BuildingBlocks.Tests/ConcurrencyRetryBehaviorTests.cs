using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Persistence.Behaviors;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class ConcurrencyRetryBehaviorTests
{
    [Fact]
    public async Task Retries_after_concurrency_conflict_and_clears_the_change_tracker_before_rerun()
    {
        await using var db = CreateDbContext();
        db.Entities.Attach(new TestEntity { Id = 1, Name = "conflict" });
        db.Entities.Attach(new TestEntity { Id = 2, Name = "stale sibling" });

        var attempts = 0;
        var behavior = CreateBehavior();

        var result = await behavior.Handle(new TestCommand(), () =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw ConcurrencyException(db, db.Entities.Local.Single(x => x.Id == 1));
            }

            db.ChangeTracker.Entries().ShouldBeEmpty();
            return Task.FromResult("retried");
        }, CancellationToken.None);

        result.ShouldBe("retried");
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_surfaces_the_concurrency_exception()
    {
        await using var db = CreateDbContext();
        var attempts = 0;
        var behavior = CreateBehavior();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => behavior.Handle(new TestCommand(), () =>
        {
            attempts++;
            var entity = new TestEntity { Id = attempts, Name = "conflict" };
            db.Entities.Attach(entity);
            throw ConcurrencyException(db, entity);
        }, CancellationToken.None));

        attempts.ShouldBe(6);
    }

    private static ConcurrencyRetryBehavior<TestCommand, string> CreateBehavior() =>
        new(NullLogger<ConcurrencyRetryBehavior<TestCommand, string>>.Instance);

    private static TestDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"concurrency-retry-{Guid.CreateVersion7():N}")
            .Options);

    private static DbUpdateConcurrencyException ConcurrencyException(TestDbContext db, TestEntity entity)
    {
        var entityEntry = db.Entry(entity);
        entityEntry.State = EntityState.Modified;
        var entry = (IUpdateEntry)entityEntry.GetType()
            .GetProperty("InternalEntry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(entityEntry)!;
        return new DbUpdateConcurrencyException("Simulated xmin conflict.", [entry]);
    }

    private sealed record TestCommand;

    private sealed class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();
    }
}
