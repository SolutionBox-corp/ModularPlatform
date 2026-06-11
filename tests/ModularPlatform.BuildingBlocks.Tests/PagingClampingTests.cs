using ModularPlatform.Cqrs;
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
}
