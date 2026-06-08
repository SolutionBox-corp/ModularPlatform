using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;

namespace ModularPlatform.Billing.Gdpr;

/// <summary>
/// GDPR erasure port for Billing. Billing holds NO direct PII: the only identifier it stores is the
/// subject's <c>UserId</c> on the wallet, which is itself the subject key the Gdpr module crypto-shreds.
/// <para>
/// The append-only credit ledger (<c>credit_accounts</c> / <c>credit_entries</c> / <c>credit_holds</c> /
/// <c>credit_buckets</c>) MUST be retained even after an erasure request: it is financial record-keeping
/// required for AML and tax/accounting obligations, which legally override the right to erasure for these
/// rows. Physically deleting ledger rows would also break the append-only money-correctness invariants the
/// platform relies on. Erasure is therefore satisfied by destroying the subject's encryption key in the
/// Gdpr module (crypto-shredding), not by touching the ledger here.
/// </para>
/// <para>
/// This eraser anonymizes any free-text / PII columns Billing might hold. Today there are none (the ledger
/// stores only amounts, types, ids and timestamps — no names, emails or notes), so the operation is a
/// documented near-no-op. It is implemented with EF / LINQ only (no raw SQL); if a free-text column is ever
/// added to Billing, anonymize it here with a tracked load + save or an atomic <c>ExecuteUpdate</c>.
/// </para>
/// </summary>
internal sealed class BillingPersonalDataEraser(BillingDbContext db) : IErasePersonalData
{
    public string ModuleName => "Billing";

    public Task EraseAsync(Guid userId, CancellationToken ct)
    {
        // No PII to anonymize: Billing stores only the UserId (the crypto-shredded subject key) plus
        // numeric ledger data that must survive for AML/tax. The append-only ledger is intentionally
        // retained; erasure is effected by the Gdpr module destroying the subject's DEK.
        _ = db;
        _ = userId;
        return Task.CompletedTask;
    }
}
