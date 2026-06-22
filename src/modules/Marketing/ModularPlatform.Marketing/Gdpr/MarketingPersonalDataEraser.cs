using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Gdpr;

/// <summary>
/// GDPR erasure port for Marketing. Every Marketing row the subject owns is their own personal data with NO
/// append-only retention requirement (no financial ledger, no AML/tax obligation like Billing): the vibe-marketing
/// chat threads + messages are the subject's free text, and the pulls / metric snapshots / analyses are their private
/// marketing data. So — mirroring the Files eraser — they are DELETED outright rather than anonymized in place. The
/// subject's DEK is additionally crypto-shredded by the Gdpr module, killing any audit-trail PII the per-module audit
/// table captured for the auditable entities.
/// <para>
/// Idempotent: a re-run (the erasure fan-out is multi-transaction and may retry) finds no rows and does nothing.
/// Soft-deleted vibe conversations are erased too (<see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>),
/// so a hidden thread is not left behind. Runs in the Worker's system context (no tenant), so the tenant query filter
/// does not restrict the match. EF / LINQ only (no raw SQL).
/// </para>
/// </summary>
internal sealed class MarketingPersonalDataEraser(MarketingDbContext db) : IErasePersonalData
{
    public string ModuleName => "Marketing";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        await db.VibeMessages.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.VibeConversations.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MarketingAnalyses.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MetricSnapshots.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.DataPulls.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
