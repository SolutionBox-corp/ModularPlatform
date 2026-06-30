using ModularPlatform.Cqrs;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Persistence;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

/// <summary>Pure-unit guards for the list-paging envelope (<see cref="PageRequest"/> clamping + <see cref="PagedResponse{T}"/> page math).</summary>
public sealed class PagingClampingTests
{
    [Fact]
    public void Page_below_one_clamps_to_one()
    {
        new PageRequest(0, 20).Page.ShouldBe(1);
        new PageRequest(-5, 20).Page.ShouldBe(1);
        new PageRequest(null, 20).Page.ShouldBe(1);
    }

    [Fact]
    public void Page_at_or_above_one_is_kept_verbatim()
    {
        new PageRequest(1, 20).Page.ShouldBe(1);
        new PageRequest(7, 20).Page.ShouldBe(7);
    }

    [Fact]
    public void PageSize_null_uses_the_default()
    {
        new PageRequest(1, null).PageSize.ShouldBe(PageRequest.DefaultPageSize);
    }

    [Fact]
    public void PageSize_below_one_clamps_to_one()
    {
        new PageRequest(1, 0).PageSize.ShouldBe(1);
        new PageRequest(1, -3).PageSize.ShouldBe(1);
    }

    [Fact]
    public void PageSize_over_the_max_clamps_to_the_max()
    {
        new PageRequest(1, PageRequest.MaxPageSize + 1).PageSize.ShouldBe(PageRequest.MaxPageSize);
        new PageRequest(1, 10_000).PageSize.ShouldBe(PageRequest.MaxPageSize);
    }

    [Fact]
    public void PageSize_in_range_is_kept_verbatim()
    {
        new PageRequest(1, 50).PageSize.ShouldBe(50);
        new PageRequest(1, PageRequest.MaxPageSize).PageSize.ShouldBe(PageRequest.MaxPageSize);
    }

    [Fact]
    public void Skip_is_zero_based_offset_from_clamped_page_and_size()
    {
        new PageRequest(1, 20).Skip.ShouldBe(0);
        new PageRequest(3, 20).Skip.ShouldBe(40);
        new PageRequest(-9, 20).Skip.ShouldBe(0); // page<1 clamps to 1 first → Skip 0, never negative
    }

    [Fact]
    public void TotalPages_is_exact_when_count_divides_evenly()
    {
        new PagedResponse<int>([], 1, 20, 100).TotalPages.ShouldBe(5);
        new PagedResponse<int>([], 1, 20, 0).TotalPages.ShouldBe(0);
    }

    [Fact]
    public void TotalPages_rounds_up_on_a_remainder()
    {
        new PagedResponse<int>([], 1, 20, 101).TotalPages.ShouldBe(6);
        new PagedResponse<int>([], 1, 20, 1).TotalPages.ShouldBe(1);
    }

    [Fact]
    public void TotalPages_is_zero_when_page_size_is_not_positive()
    {
        new PagedResponse<int>([], 1, 0, 100).TotalPages.ShouldBe(0);
    }

    [Fact]
    public async Task ToPagedResponseAsync_counts_total_and_preserves_ordered_page()
    {
        await using var db = new PagingTestDbContext(new DbContextOptionsBuilder<PagingTestDbContext>()
            .UseInMemoryDatabase($"paging-{Guid.CreateVersion7():N}")
            .Options);
        db.Items.AddRange(
            new PagingTestItem { Name = "Charlie", SortOrder = 3 },
            new PagingTestItem { Name = "Ada", SortOrder = 1 },
            new PagingTestItem { Name = "Bob", SortOrder = 2 },
            new PagingTestItem { Name = "Dora", SortOrder = 4 });
        await db.SaveChangesAsync();

        var page = await db.Items
            .OrderBy(item => item.SortOrder)
            .Select(item => item.Name)
            .ToPagedResponseAsync(new PageRequest(page: 2, pageSize: 2), CancellationToken.None);

        page.Items.ShouldBe(["Charlie", "Dora"]);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(4);
        page.TotalPages.ShouldBe(2);
    }

    [Fact]
    public async Task ToPagedResponseAsync_rejects_unordered_queries()
    {
        await using var db = new PagingTestDbContext(new DbContextOptionsBuilder<PagingTestDbContext>()
            .UseInMemoryDatabase($"paging-unordered-{Guid.CreateVersion7():N}")
            .Options);
        db.Items.AddRange(
            new PagingTestItem { Name = "Charlie", SortOrder = 3 },
            new PagingTestItem { Name = "Ada", SortOrder = 1 });
        await db.SaveChangesAsync();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => db.Items
            .Select(item => item.Name)
            .ToPagedResponseAsync(new PageRequest(page: 1, pageSize: 20), CancellationToken.None));

        exception.Message.ShouldContain("OrderBy");
    }

    [Fact]
    public async Task ToPagedResponseAsync_rejects_ordering_that_was_applied_before_a_shape_changing_join()
    {
        await using var db = new PagingTestDbContext(new DbContextOptionsBuilder<PagingTestDbContext>()
            .UseInMemoryDatabase($"paging-join-{Guid.CreateVersion7():N}")
            .Options);
        var item = new PagingTestItem { Name = "Ada", SortOrder = 1 };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        db.Tags.Add(new PagingTestTag { ItemId = item.Id, Value = "first" });
        await db.SaveChangesAsync();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => db.Items
            .OrderBy(i => i.SortOrder)
            .Join(
                db.Tags,
                item => item.Id,
                tag => tag.ItemId,
                (item, tag) => new { item.Name, tag.Value })
            .ToPagedResponseAsync(new PageRequest(page: 1, pageSize: 20), CancellationToken.None));

        exception.Message.ShouldContain("OrderBy");
    }

    private sealed class PagingTestDbContext(DbContextOptions<PagingTestDbContext> options) : DbContext(options)
    {
        public DbSet<PagingTestItem> Items => Set<PagingTestItem>();
        public DbSet<PagingTestTag> Tags => Set<PagingTestTag>();
    }

    private sealed class PagingTestItem
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class PagingTestTag
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public required string Value { get; set; }
    }
}
