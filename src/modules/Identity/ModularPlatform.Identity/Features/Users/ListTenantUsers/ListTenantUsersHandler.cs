using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Features.Users.ListTenantUsers;

internal sealed class ListTenantUsersHandler(IReadDbContextFactory<IdentityDbContext> readFactory)
    : IQueryHandler<ListTenantUsersQuery, PagedResponse<TenantUserListItem>>
{
    public async Task<PagedResponse<TenantUserListItem>> Handle(ListTenantUsersQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);
        var filtered = db.Users.Where(u => EF.Property<Guid?>(u, "TenantId") == query.TenantId);

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(u => new TenantUserListItem(u.Id, u.Email, u.DisplayName))
            .ToListAsync(ct);

        return new PagedResponse<TenantUserListItem>(items, paging.Page, paging.PageSize, total);
    }
}
