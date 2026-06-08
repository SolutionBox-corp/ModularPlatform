using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Persistence;

namespace ModularPlatform.Gdpr.Features.Erasure.ShredSubjectKey;

/// <summary>
/// Crypto-shreds the subject's data-encryption key: drops <c>WrappedDek</c> and stamps <c>DeletedAt</c>.
/// Once the DEK is gone, every ciphertext encrypted under it is permanently unrecoverable — this is the
/// authoritative GDPR erasure act, effective even against append-only stores and immutable backups.
/// <para>
/// Pure DB write (no integration event) → injects the scoped DbContext directly and SaveChangesAsync, exactly
/// like <c>WithdrawConsentHandler</c>. EF / LINQ only, no raw SQL. Idempotent: an already-shredded key
/// (<c>DeletedAt</c> set) is left untouched, and a missing key row is a no-op (nothing was ever encrypted for
/// the subject). The tracked load + save keeps the xmin concurrency token and the per-module audit entry.
/// </para>
/// </summary>
internal sealed class ShredSubjectKeyHandler(GdprDbContext db, IClock clock)
    : ICommandHandler<ShredSubjectKeyCommand>
{
    public async Task<Unit> Handle(ShredSubjectKeyCommand command, CancellationToken ct)
    {
        var subjectKey = await db.SubjectKeys
            .FirstOrDefaultAsync(k => k.UserId == command.UserId, ct);

        if (subjectKey is { DeletedAt: null })
        {
            subjectKey.WrappedDek = null;
            subjectKey.DeletedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
