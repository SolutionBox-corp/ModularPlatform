using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Gdpr;

/// <summary>
/// GDPR erasure port for CRM: scrubs the PII the user holds about their contacts and blanks interaction notes,
/// then soft-deletes the contacts. The encrypted FullName/Email/Phone ciphertext is overwritten with neutral
/// tombstones (atomic <c>ExecuteUpdate</c> — deliberately bypasses the encryption interceptor: the tombstone
/// constants are not PII and must stay readable). EF / LINQ only. Idempotent. Runs in the Worker's system context
/// (so the tenant filter is off; <c>IgnoreQueryFilters</c> also catches already soft-deleted rows still holding
/// ciphertext).
/// </summary>
internal sealed class CrmPersonalDataEraser(CrmDbContext db, IClock clock) : IErasePersonalData
{
    public string ModuleName => "Crm";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await db.Contacts
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.FullName, "[erased]")
                    .SetProperty(c => c.Email, (string?)null)
                    .SetProperty(c => c.EmailHash, (string?)null)
                    .SetProperty(c => c.Phone, (string?)null)
                    .SetProperty(c => c.Company, (string?)null)
                    .SetProperty(c => c.Position, (string?)null)
                    .SetProperty(c => c.Notes, (string?)null)
                    .SetProperty(c => c.DeletedAt, c => c.DeletedAt ?? now),
                ct);

        await db.ContactInteractions
            .IgnoreQueryFilters()
            .Where(i => i.UserId == userId && i.Body != null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Body, (string?)null), ct);

        // Meeting metadata is the user's own scheduling data — scrub the free-text fields and soft-delete.
        await db.Meetings
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Title, "[erased]")
                    .SetProperty(m => m.Location, (string?)null)
                    .SetProperty(m => m.Notes, (string?)null)
                    .SetProperty(m => m.Outcome, (string?)null)
                    .SetProperty(m => m.DeletedAt, m => m.DeletedAt ?? now),
                ct);
    }
}
