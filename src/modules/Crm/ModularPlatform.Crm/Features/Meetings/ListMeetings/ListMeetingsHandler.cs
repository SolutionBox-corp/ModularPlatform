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

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            filtered = filtered.Where(m => m.Status == status);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderBy(m => m.ScheduledAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(m => new MeetingResponse(
                m.Id, m.ContactId, m.Title, m.ScheduledAt, m.DurationMinutes, m.Location, m.Notes,
                m.Status, m.Outcome, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<MeetingResponse>(items, paging.Page, paging.PageSize, total);
    }
}
