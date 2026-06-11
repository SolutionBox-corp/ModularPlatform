using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Gdpr.Features.Consents;

/// <summary>
/// GDPR data-portability port for the Gdpr module's OWN consent log. Without it the subject's consent history
/// (their personal data, keyed by UserId) would be silently absent from the Art. 15 export. Read-only via the
/// factory. (The subject_keys envelope is deliberately NOT exported — it is the crypto-shredding key material.)
/// </summary>
internal sealed class ConsentPersonalDataExporter(IReadDbContextFactory<GdprDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Gdpr.Consents";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var consents = await db.ConsentRecords
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.RecordedAt)
            .Select(c => new { c.ConsentType, c.Granted, c.RecordedAt })
            .ToListAsync(ct);

        return new Dictionary<string, object?> { ["consents"] = consents };
    }
}
