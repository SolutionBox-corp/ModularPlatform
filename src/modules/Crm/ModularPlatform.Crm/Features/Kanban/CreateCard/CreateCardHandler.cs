using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.CreateCard;

/// <summary>Appends a card to the bottom of a column the caller owns (position = current count). Foreign column ⇒ 404.</summary>
internal sealed class CreateCardHandler(CrmDbContext db)
    : ICommandHandler<CreateCardCommand, CreateCardResponse>
{
    public async Task<CreateCardResponse> Handle(CreateCardCommand command, CancellationToken ct)
    {
        var column = await db.KanbanColumns
            .FirstOrDefaultAsync(c => c.Id == command.ColumnId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.column_not_found", "Column not found.");

        // The optional contact/deal links must belong to the caller — otherwise a card could reference a foreign row.
        if (command.ContactId is { } contactId
            && !await db.Contacts.AnyAsync(c => c.Id == contactId && c.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.contact_not_found", "Contact not found.");
        }

        if (command.DealId is { } dealId
            && !await db.Deals.AnyAsync(d => d.Id == dealId && d.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.deal_not_found", "Deal not found.");
        }

        if (command.MeetingId is { } meetingId
            && !await db.Meetings.AnyAsync(m => m.Id == meetingId && m.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");
        }

        if (command.TaskId is { } taskId
            && !await db.Tasks.AnyAsync(t => t.Id == taskId && t.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.task_not_found", "Task not found.");
        }

        var position = await db.KanbanCards.CountAsync(c => c.ColumnId == column.Id, ct);
        var card = new KanbanCard
        {
            UserId = command.UserId,
            BoardId = column.BoardId,
            ColumnId = column.Id,
            Position = position,
            Title = command.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description,
            ContactId = command.ContactId,
            DealId = command.DealId,
            MeetingId = command.MeetingId,
            TaskId = command.TaskId,
            AssigneeUserId = command.AssigneeUserId,
            Priority = string.IsNullOrWhiteSpace(command.Priority) ? TaskPriorities.Normal : command.Priority,
            Labels = NormalizeLabels(command.Labels),
            StartAt = command.StartAt?.ToUniversalTime(),
            DueAt = command.DueAt?.ToUniversalTime(),
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync(ct);
        return new CreateCardResponse(card.Id);
    }

    private static string[] NormalizeLabels(string[]? labels) =>
        labels is null
            ? []
            : labels
                .Select(label => label.Trim())
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray();
}
