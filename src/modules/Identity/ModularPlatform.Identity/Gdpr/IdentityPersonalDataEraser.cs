using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Gdpr;

/// <summary>
/// GDPR erasure port for Identity: anonymizes the subject's account PII in place and soft-deletes the user,
/// so the row's referential identity survives but no personal data remains. The email is replaced with a
/// unique non-routable token (its blind-index hash keeps UNIQUE(EmailHash) satisfied deterministically) and
/// <c>PasswordHash</c> is blanked — an erased account fails login on CREDENTIALS, not merely on the
/// non-routable address. The encrypted Email/DisplayName ciphertext is additionally killed for good when the
/// erasure flow shreds the subject's DEK right after this scrub.
/// EF / LINQ only (atomic <c>ExecuteUpdate</c> — deliberately bypasses the encryption interceptor: the
/// tombstone constants are not PII and must stay readable). Idempotent. Runs in the Worker's system context.
/// </summary>
internal sealed class IdentityPersonalDataEraser(IdentityDbContext db, IBlindIndexHasher blindIndex)
    : IErasePersonalData
{
    public string ModuleName => "Identity";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var token = $"erased-{userId:N}@erased.invalid";
        var tokenHash = blindIndex.Hash(token.ToUpperInvariant());

        await db.Users
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.Email, token)
                    .SetProperty(u => u.EmailHash, tokenHash)
                    .SetProperty(u => u.DisplayName, (string?)null)
                    .SetProperty(u => u.PasswordHash, string.Empty)
                    .SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow),
                ct);
    }
}
