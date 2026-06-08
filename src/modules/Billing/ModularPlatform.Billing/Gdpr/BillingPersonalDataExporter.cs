using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Gdpr;

/// <summary>
/// GDPR data-portability port for Billing. Returns the subject's wallet + full append-only ledger (the ledger
/// is never physically deleted on erasure — it is anonymized/retained for AML/tax). Read-only via the factory.
/// </summary>
internal sealed class BillingPersonalDataExporter(IReadDbContextFactory<BillingDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Billing";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var account = await db.CreditAccounts
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.Posted, a.Pending, a.Available })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return new Dictionary<string, object?>
            {
                ["account"] = null,
                ["entries"] = Array.Empty<object>(),
            };
        }

        var entries = await db.CreditEntries
            .Where(e => e.AccountId == account.Id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new
            {
                e.Direction,
                e.Amount,
                e.Type,
                e.TransactionId,
                e.BucketId,
                e.IdempotencyKey,
                e.CreatedAt,
            })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["account"] = new
            {
                account.Id,
                account.Posted,
                account.Pending,
                account.Available,
            },
            ["entries"] = entries,
        };
    }
}
