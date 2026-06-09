using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Persistence;

/// <summary>EF helper that turns an ordered <see cref="IQueryable{T}"/> into a <see cref="PagedResponse{T}"/>.</summary>
public static class PagedQueryExtensions
{
    /// <summary>
    /// Counts the full set, then returns the requested page. Apply your <c>Where</c>/<c>OrderBy</c> first — the
    /// query MUST be ordered for stable paging.
    /// </summary>
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken ct)
    {
        var totalCount = await query.LongCountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResponse<T>(items, page.Page, page.PageSize, totalCount);
    }
}
