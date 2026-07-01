using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.ListMachineTokens;

internal sealed class ListMachineTokensHandler(
    IReadDbContextFactory<IdentityDbContext> readFactory,
    IServiceProvider services,
    IClock clock)
    : IQueryHandler<ListMachineTokensQuery, ListMachineTokensResponse>
{
    public async Task<ListMachineTokensResponse> Handle(ListMachineTokensQuery query, CancellationToken ct)
    {
        var directory = services.GetService<ITenantDirectory>();
        if (directory is not null && await directory.GetByIdAsync(query.TenantId, ct) is null)
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        await using var db = readFactory.Create();
        var now = clock.UtcNow;
        var items = await db.MachineTokenIssuances
            .Where(t => t.TargetTenantId == query.TenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new MachineTokenSummary(
                t.Id,
                t.MachineSubjectId,
                t.Name,
                t.RevokedAt != null ? "Revoked" : t.ExpiresAt <= now ? "Expired" : "Active",
                t.CreatedAt,
                t.ExpiresAt,
                t.RevokedAt))
            .ToListAsync(ct);

        return new ListMachineTokensResponse(items);
    }
}
