using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Gdpr.Persistence;

namespace ModularPlatform.Gdpr.Features.Consents;

/// <summary>
/// GDPR erasure port for the Gdpr module's OWN consent log. Consent records are the subject's personal data
/// (keyed by their real UserId) and — unlike the AML/tax credit ledger — carry NO overriding legal-retention
/// obligation, so a "forget me" request DELETES them rather than retaining them. Without this, an erased subject's
/// consent history survived with their real UserId.
/// <para>
/// <c>ExecuteDelete</c> is the sanctioned GDPR-scrub path (the audit/xmin bypass is intentional — we are erasing,
/// not auditing, this subject). EF / LINQ only.
/// </para>
/// </summary>
internal sealed class ConsentPersonalDataEraser(GdprDbContext db) : IErasePersonalData
{
    public string ModuleName => "Gdpr.Consents";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        await db.ConsentRecords.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
