using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Features.Admin.RevokeTenantInvite;

internal sealed class RevokeTenantInviteHandler(TenancyDbContext db, IClock clock)
    : ICommandHandler<RevokeTenantInviteCommand, RevokeTenantInviteResponse>
{
    public async Task<RevokeTenantInviteResponse> Handle(RevokeTenantInviteCommand command, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct))
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        var invite = await db.TenantInvites.FirstOrDefaultAsync(
                i => i.TenantId == command.TenantId && i.Id == command.InviteId,
                ct)
            ?? throw new NotFoundException("tenant.invite_not_found", "Invite not found.");

        if (invite.RevokedAt is null && invite.ConsumedAt is null)
        {
            invite.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var status = invite.RevokedAt is not null
            ? "Revoked"
            : invite.ConsumedAt is not null
                ? "Consumed"
                : "Pending";

        return new RevokeTenantInviteResponse(invite.Id, status, invite.RevokedAt);
    }
}
