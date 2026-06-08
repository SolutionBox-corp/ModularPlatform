using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Gdpr;

/// <summary>
/// GDPR erasure port for Identity: anonymizes the subject's account PII in place (email, display name) and
/// soft-deletes the user, so the row's referential identity survives but no personal data remains. The email is
/// replaced with a unique non-routable token to preserve the UNIQUE(NormalizedEmail) constraint.
/// EF / LINQ only (atomic <c>ExecuteUpdate</c>); idempotent. Runs in the Worker's system context.
/// <para>
/// NOTE: this scrubs the LIVE row only. Plaintext PII previously written to <c>identity_audit_entries</c> is NOT
/// rewritten — full audit-PII erasure requires the crypto-shredding-at-rest path (an open platform decision).
/// </para>
/// </summary>
internal sealed class IdentityPersonalDataEraser(IdentityDbContext db) : IErasePersonalData
{
    public string ModuleName => "Identity";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var token = $"erased-{userId:N}@erased.invalid";

        await db.Users
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.Email, token)
                    .SetProperty(u => u.NormalizedEmail, token.ToUpperInvariant())
                    .SetProperty(u => u.DisplayName, (string?)null)
                    .SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow),
                ct);
    }
}
