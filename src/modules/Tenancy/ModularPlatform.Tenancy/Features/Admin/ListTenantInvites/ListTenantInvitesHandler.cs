using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenantInvites;

internal sealed class ListTenantInvitesHandler(IReadDbContextFactory<TenancyDbContext> readFactory, IClock clock)
    : IQueryHandler<ListTenantInvitesQuery, TenantInvitesResponse>
{
    public async Task<TenantInvitesResponse> Handle(ListTenantInvitesQuery query, CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);
        var statusFilter = query.Status?.Trim();
        var now = clock.UtcNow;

        await using var db = readFactory.Create();

        if (!await db.Tenants.AnyAsync(t => t.Id == query.TenantId, ct))
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        var invites = db.TenantInvites.Where(i => i.TenantId == query.TenantId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (!Enum.TryParse<TenantInviteStatus>(statusFilter, ignoreCase: true, out var status))
            {
                return new TenantInvitesResponse([], 0, limit, offset);
            }

            invites = status switch
            {
                TenantInviteStatus.Revoked => invites.Where(i => i.RevokedAt != null),
                TenantInviteStatus.Consumed => invites.Where(i => i.RevokedAt == null && i.ConsumedAt != null),
                TenantInviteStatus.Expired => invites.Where(i =>
                    i.RevokedAt == null && i.ConsumedAt == null && i.ExpiresAt <= now),
                TenantInviteStatus.Pending => invites.Where(i =>
                    i.RevokedAt == null && i.ConsumedAt == null && i.ExpiresAt > now),
                _ => invites,
            };
        }

        var total = await invites.CountAsync(ct);

        var items = await invites
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip(offset)
            .Take(limit)
            .Select(i => new
            {
                i.Id,
                i.CreatedAt,
                i.ExpiresAt,
                i.ConsumedAt,
                i.RevokedAt,
            })
            .ToListAsync(ct);

        return new TenantInvitesResponse(
            items.Select(i => new TenantInviteItem(
                    i.Id,
                    StatusOf(i.ConsumedAt, i.RevokedAt, i.ExpiresAt, now).ToString(),
                    i.CreatedAt,
                    i.ExpiresAt,
                    i.ConsumedAt,
                    i.RevokedAt))
                .ToList(),
            total,
            limit,
            offset);
    }

    private static TenantInviteStatus StatusOf(
        DateTimeOffset? consumedAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (revokedAt is not null)
        {
            return TenantInviteStatus.Revoked;
        }

        if (consumedAt is not null)
        {
            return TenantInviteStatus.Consumed;
        }

        return expiresAt <= now ? TenantInviteStatus.Expired : TenantInviteStatus.Pending;
    }
}

internal enum TenantInviteStatus
{
    Pending,
    Consumed,
    Expired,
    Revoked,
}
