using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// <see cref="ITenantRegistrationGate"/> — enforces a tenant's <see cref="TenantRegistrationMode"/> when a user
/// signs up on its subdomain. Runs in the ANONYMOUS registration context (no tenant claim); the registry tables are
/// not tenant-scoped, so it filters by the explicit tenant id. For <c>InviteOnly</c> it consumes a single-use invite
/// by stamping <c>ConsumedAt</c> — a concurrent reuse of the same token loses the xmin race and is retried by the
/// registration command, which then re-evaluates and sees the invite already consumed (deny).
/// </summary>
internal sealed class TenantRegistrationGate(TenancyDbContext db, IClock clock) : ITenantRegistrationGate
{
    public async Task<bool> TryAcceptJoinAsync(Guid tenantId, string? inviteToken, CancellationToken ct = default)
    {
        var mode = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantRegistrationMode?)t.RegistrationMode)
            .FirstOrDefaultAsync(ct);

        switch (mode)
        {
            case null:                              // unknown tenant — never allow a join
            case TenantRegistrationMode.Closed:
                return false;

            case TenantRegistrationMode.Open:
                return true;

            case TenantRegistrationMode.InviteOnly:
                if (string.IsNullOrWhiteSpace(inviteToken))
                {
                    return false;
                }

                var hash = HashToken(inviteToken);
                var now = clock.UtcNow;
                var invite = await db.TenantInvites.FirstOrDefaultAsync(
                    i => i.TenantId == tenantId
                        && i.TokenHash == hash
                        && i.ConsumedAt == null
                        && i.RevokedAt == null
                        && i.ExpiresAt > now,
                    ct);
                if (invite is null)
                {
                    return false;
                }

                invite.ConsumedAt = now;            // single-use: xmin serializes a concurrent reuse
                await db.SaveChangesAsync(ct);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The stored form of an invite token — a SHA-256 hex digest, like a refresh token. The raw token is
    /// shown once to the inviting admin and never persisted.</summary>
    internal static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
