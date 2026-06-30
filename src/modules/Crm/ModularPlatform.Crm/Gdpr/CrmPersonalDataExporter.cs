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
                c.CompanyId,
                c.FirstName,
                c.LastName,
                c.Email,
                c.Phone,
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

        var companies = await db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Domain,
                c.Industry,
                c.IdentificationNumber,
                c.TaxIdentificationNumber,
                c.RegisteredAddress,
                c.City,
                c.PostalCode,
                c.Country,
                c.Notes,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        var boards = await db.KanbanBoards.IgnoreQueryFilters().Where(b => b.UserId == userId)
            .Select(b => new { b.Id, b.Name, b.CreatedAt }).ToListAsync(ct);
        var cards = await db.KanbanCards.IgnoreQueryFilters().Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.BoardId, c.ColumnId, c.Title, c.Description, c.ContactId, c.DealId, c.DueAt, c.CreatedAt }).ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["contacts"] = contacts,
            ["interactions"] = interactions,
            ["meetings"] = meetings,
            ["deals"] = deals,
            ["tasks"] = tasks,
            ["companies"] = companies,
            ["boards"] = boards,
            ["cards"] = cards,
        };
    }
}
