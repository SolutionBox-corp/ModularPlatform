using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.ListMeetings;

internal sealed class ListMeetingsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListMeetingsQuery, MeetingsPageResponse>
{
    public async Task<MeetingsPageResponse> Handle(ListMeetingsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

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
            .Skip(offset)
            .Take(limit)
            .Select(m => new MeetingResponse(
                m.Id, m.ContactId, m.Title, m.ScheduledAt, m.DurationMinutes, m.Location, m.Notes,
                m.Status, m.Outcome, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);

        return new MeetingsPageResponse(items, total, limit, offset);
    }
}
