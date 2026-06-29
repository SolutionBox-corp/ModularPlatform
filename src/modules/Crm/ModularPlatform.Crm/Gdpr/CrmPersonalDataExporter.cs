using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Gdpr;

/// <summary>
/// GDPR data-portability for CRM: the user's contacts and logged interactions (PII decrypted via the read
/// factory's model converter). The Gdpr module fans these exports into one document.
/// </summary>
internal sealed class CrmPersonalDataExporter(IReadDbContextFactory<CrmDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Crm";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var contacts = await db.Contacts
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .Select(c => new
            {
                c.Id,
                c.FullName,
                c.Email,
                c.Phone,
                c.Company,
                c.Position,
                c.Notes,
                c.Tags,
                c.Status,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        var interactions = await db.ContactInteractions
            .IgnoreQueryFilters()
            .Where(i => i.UserId == userId)
            .Select(i => new { i.Id, i.ContactId, i.Type, i.OccurredAt, i.Body })
            .ToListAsync(ct);

        var meetings = await db.Meetings
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .Select(m => new
            {
                m.Id,
                m.ContactId,
                m.Title,
                m.ScheduledAt,
                m.DurationMinutes,
                m.Location,
                m.Notes,
                m.Status,
                m.Outcome,
                m.CreatedAt,
            })
            .ToListAsync(ct);

        var deals = await db.Deals
            .IgnoreQueryFilters()
            .Where(d => d.UserId == userId)
            .Select(d => new
            {
                d.Id,
                d.ContactId,
                d.Title,
                d.AmountCents,
                d.Currency,
                d.Stage,
                d.ExpectedCloseAt,
                d.ClosedAt,
                d.Notes,
                d.CreatedAt,
            })
            .ToListAsync(ct);

        var tasks = await db.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                t.ContactId,
                t.DealId,
                t.Title,
                t.Description,
                t.DueAt,
                t.Priority,
                t.Status,
                t.CompletedAt,
                t.CreatedAt,
            })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["contacts"] = contacts,
            ["interactions"] = interactions,
            ["meetings"] = meetings,
            ["deals"] = deals,
            ["tasks"] = tasks,
        };
    }
}
