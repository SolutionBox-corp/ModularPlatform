using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Features.PlatformAdmin.ListPlatformUsers;

/// <summary>
/// CROSS-TENANT user listing for platform admins. <see cref="DbSet{TEntity}"/> normally carries the per-tenant
/// query filter (<c>IsSystem || TenantId == claim</c>) AND the soft-delete filter; <c>IgnoreQueryFilters()</c>
/// drops BOTH, so the soft-delete guard (<c>DeletedAt == null</c>) is re-added explicitly. The optional
/// <c>TenantId</c> narrows to a single tenant via the shadow <c>TenantId</c> property.
/// </summary>
internal sealed class ListPlatformUsersHandler(IReadDbContextFactory<IdentityDbContext> readFactory)
    : IQueryHandler<ListPlatformUsersQuery, PlatformUsersResponse>
{
    public async Task<PlatformUsersResponse> Handle(ListPlatformUsersQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

        var filtered = db.Users
            .IgnoreQueryFilters()
            .Where(u => u.DeletedAt == null);

        if (query.TenantId is { } tenantId)
        {
            filtered = filtered.Where(u => EF.Property<Guid?>(u, "TenantId") == tenantId);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(u => u.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(u => new PlatformUserItem(
                u.Id,
                u.Email,
                u.DisplayName,
                EF.Property<Guid?>(u, "TenantId"),
                u.CreatedAt))
            .ToListAsync(ct);

        return new PlatformUsersResponse(items, total, limit, offset);
    }
}
