using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Entities;
using ModularPlatform.Gdpr.Persistence;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

/// <summary>
/// Records consent as WITHDRAWN by appending a new <see cref="ConsentRecord"/> (append-only — never
/// updates an existing row). Pure DB write (no integration event) → injects the scoped DbContext.
/// </summary>
internal sealed class WithdrawConsentHandler(GdprDbContext db, IClock clock)
    : ICommandHandler<WithdrawConsentCommand, WithdrawConsentResponse>
{
    public async Task<WithdrawConsentResponse> Handle(WithdrawConsentCommand command, CancellationToken ct)
    {
        var record = new ConsentRecord
        {
            UserId = command.UserId,
            ConsentType = command.ConsentType.Trim(),
            Granted = false,
            RecordedAt = clock.UtcNow,
            PolicyVersion = command.PolicyVersion?.Trim(),
        };

        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return new WithdrawConsentResponse(record.Id);
    }
}
