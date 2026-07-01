using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.RevokeMachineToken;

internal sealed class RevokeMachineTokenHandler(IdentityDbContext db, IClock clock)
    : ICommandHandler<RevokeMachineTokenCommand, RevokeMachineTokenResponse>
{
    public async Task<RevokeMachineTokenResponse> Handle(RevokeMachineTokenCommand command, CancellationToken ct)
    {
        var token = await db.MachineTokenIssuances
            .FirstOrDefaultAsync(t => t.Id == command.TokenId && t.TargetTenantId == command.TenantId, ct)
            ?? throw new NotFoundException("machine_token.not_found", "Machine token not found.");

        if (token.RevokedAt is null)
        {
            token.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new RevokeMachineTokenResponse(token.Id, "Revoked", token.RevokedAt);
    }
}
