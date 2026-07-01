using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Security;

internal sealed class MachineTokenRegistry(IdentityDbContext db, IClock clock) : IMachineTokenRegistry
{
    public Task<bool> IsActiveAsync(string tokenId, Guid machineSubjectId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return db.MachineTokenIssuances
            .AsNoTracking()
            .AnyAsync(t =>
                t.TokenId == tokenId
                && t.MachineSubjectId == machineSubjectId
                && t.RevokedAt == null
                && t.ExpiresAt > now, ct);
    }
}
