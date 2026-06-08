using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Entities;
using ModularPlatform.Gdpr.Persistence;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

/// <summary>
/// Records consent as GRANTED by appending a new <see cref="ConsentRecord"/> (append-only — never
/// updates an existing row). Pure DB write (no integration event) → injects the scoped DbContext.
/// </summary>
internal sealed class GrantConsentHandler(GdprDbContext db, IClock clock)
    : ICommandHandler<GrantConsentCommand, GrantConsentResponse>
{
    public async Task<GrantConsentResponse> Handle(GrantConsentCommand command, CancellationToken ct)
    {
        var record = new ConsentRecord
        {
            UserId = command.UserId,
            ConsentType = command.ConsentType.Trim(),
            Granted = true,
            RecordedAt = clock.UtcNow,
        };

        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return new GrantConsentResponse(record.Id);
    }
}
