using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Features.Auth.PurgeRefreshTokens;

/// <summary>
/// Deletes refresh tokens whose <c>ExpiresAt</c> is older than the retention window — an expired token can neither
/// authenticate nor be meaningfully reuse-detected (the family is already past its sliding window), so retaining it
/// only bloats the table + backups. Set-based <c>ExecuteDelete</c> (refresh_tokens carry no PII, so bypassing the
/// audit/xmin path is correct — the GDPR eraser already updates them set-based).
/// </summary>
internal sealed class PurgeRefreshTokensHandler(
    IdentityDbContext db, IClock clock, IOptions<IdentityAuthOptions> options)
    : ICommandHandler<PurgeRefreshTokensCommand>
{
    public async Task<Unit> Handle(PurgeRefreshTokensCommand command, CancellationToken ct)
    {
        var retentionDays = options.Value.RefreshTokenRetentionDays > 0 ? options.Value.RefreshTokenRetentionDays : 30;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);

        return Unit.Value;
    }
}
