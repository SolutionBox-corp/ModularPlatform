using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using System.Linq.Expressions;

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
        if (!ContainsOrdering(query.Expression))
        {
            throw new InvalidOperationException(
                "Paged queries must apply OrderBy/OrderByDescending before ToPagedResponseAsync to keep paging stable.");
        }

        var totalCount = await query.LongCountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResponse<T>(items, page.Page, page.PageSize, totalCount);
    }

    private static bool ContainsOrdering(Expression expression)
    {
        while (expression is MethodCallExpression methodCall)
        {
            if (methodCall.Method.DeclaringType != typeof(Queryable))
            {
                return false;
            }

            if (methodCall.Method.Name is nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.ThenByDescending))
            {
                return true;
            }

            if (methodCall.Method.Name is nameof(Queryable.Where) or nameof(Queryable.Select))
            {
                expression = methodCall.Arguments[0];
                continue;
            }

            return false;
        }

        return expression is UnaryExpression unary && ContainsOrdering(unary.Operand);
    }
}
