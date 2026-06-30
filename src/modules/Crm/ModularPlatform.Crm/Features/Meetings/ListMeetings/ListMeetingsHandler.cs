using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.ListMeetings;

internal sealed class ListMeetingsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListMeetingsQuery, PagedResponse<MeetingResponse>>
{
    public async Task<PagedResponse<MeetingResponse>> Handle(ListMeetingsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.Meetings.Where(m => m.UserId == query.UserId);

        if (query.From is { } from)
        {
            filtered = filtered.Where(m => m.ScheduledAt >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(m => m.ScheduledAt <= to);
        }

        if (query.ContactId is { } contactId)
        {
            filtered = filtered.Where(m => m.ContactId == contactId);
        }

        if (query.CompanyId is { } companyId)
        {
            filtered = filtered.Where(m =>
                m.ContactId != null
                && db.Contacts.Any(c => c.Id == m.ContactId && c.UserId == query.UserId && c.CompanyId == companyId));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            filtered = filtered.Where(m => m.Status == status);
        }

        var total = await filtered.CountAsync(ct);

        var rows = await filtered
            .OrderBy(m => m.ScheduledAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(m => new
            {
                m.Id,
                m.ContactId,
                ContactFirstName = db.Contacts
                    .Where(c => c.Id == m.ContactId && c.UserId == query.UserId)
                    .Select(c => c.FirstName)
                    .FirstOrDefault(),
                ContactLastName = db.Contacts
                    .Where(c => c.Id == m.ContactId && c.UserId == query.UserId)
                    .Select(c => c.LastName)
                    .FirstOrDefault(),
                m.Title,
                m.ScheduledAt,
                m.DurationMinutes,
                m.Location,
                m.Notes,
                m.Status,
                m.Outcome,
                m.CreatedAt,
                m.UpdatedAt,
            })
            .ToListAsync(ct);

        var items = rows.Select(m => new MeetingResponse(
                m.Id,
                m.ContactId,
                FormatContactName(m.ContactFirstName, m.ContactLastName),
                m.Title,
                m.ScheduledAt,
                m.DurationMinutes,
                m.Location,
                m.Notes,
                m.Status,
                m.Outcome,
                m.CreatedAt,
                m.UpdatedAt))
            .ToList();

        return new PagedResponse<MeetingResponse>(items, paging.Page, paging.PageSize, total);
    }

    private static string? FormatContactName(string? firstName, string? lastName)
    {
        var name = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
