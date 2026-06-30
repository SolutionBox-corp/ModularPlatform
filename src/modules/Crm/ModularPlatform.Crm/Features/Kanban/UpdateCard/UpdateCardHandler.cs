using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.UpdateCard;

internal sealed class UpdateCardHandler(CrmDbContext db)
    : ICommandHandler<UpdateCardCommand, KanbanCardDto>
{
    public async Task<KanbanCardDto> Handle(UpdateCardCommand command, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .FirstOrDefaultAsync(c => c.Id == command.CardId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.card_not_found", "Card not found.");

        if (command.ContactIdSet && command.ContactId is { } contactId
            && !await db.Contacts.AnyAsync(c => c.Id == contactId && c.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.contact_not_found", "Contact not found.");
        }

        if (command.DealIdSet && command.DealId is { } dealId
            && !await db.Deals.AnyAsync(d => d.Id == dealId && d.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.deal_not_found", "Deal not found.");
        }

        if (command.MeetingIdSet && command.MeetingId is { } meetingId
            && !await db.Meetings.AnyAsync(m => m.Id == meetingId && m.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");
        }

        if (command.TaskIdSet && command.TaskId is { } taskId
            && !await db.Tasks.AnyAsync(t => t.Id == taskId && t.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.task_not_found", "Task not found.");
        }

        if (command.Title is not null)
        {
            card.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            card.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description;
        }

        if (command.ContactIdSet)
        {
            card.ContactId = command.ContactId;
        }

        if (command.DealIdSet)
        {
            card.DealId = command.DealId;
        }

        if (command.MeetingIdSet)
        {
            card.MeetingId = command.MeetingId;
        }

        if (command.TaskIdSet)
        {
            card.TaskId = command.TaskId;
        }

        if (command.AssigneeUserIdSet)
        {
            card.AssigneeUserId = command.AssigneeUserId;
        }

        if (command.Priority is not null)
        {
            card.Priority = command.Priority;
        }

        if (command.Labels is not null)
        {
            card.Labels = NormalizeLabels(command.Labels);
        }

        if (command.StartAt is not null)
        {
            card.StartAt = command.StartAt.Value.ToUniversalTime();
        }

        if (command.DueAt is not null)
        {
            card.DueAt = command.DueAt.Value.ToUniversalTime();
        }

        if (card.TaskId is { } linkedTaskId)
        {
            var task = await db.Tasks
                .FirstOrDefaultAsync(t => t.Id == linkedTaskId && t.UserId == command.UserId, ct);
            if (task is not null)
            {
                task.Title = card.Title;
                task.Description = card.Description;
                task.DueAt = card.DueAt;
                task.Priority = card.Priority;
            }
        }

        await db.SaveChangesAsync(ct);

        return new KanbanCardDto(
            card.Id, card.ColumnId, card.Position, card.Title, card.Description, card.ContactId, card.DealId,
            card.MeetingId, card.TaskId, card.AssigneeUserId, card.Priority, card.Labels, card.StartAt, card.DueAt);
    }

    private static string[] NormalizeLabels(string[] labels) =>
        labels
            .Select(label => label.Trim())
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
}
