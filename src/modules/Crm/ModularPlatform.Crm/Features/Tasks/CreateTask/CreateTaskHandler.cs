using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Tasks.CreateTask;

/// <summary>
/// Write slice WITHOUT an event. If a contact and/or deal is linked, each must be owned (foreign/missing ⇒ 404).
/// Owner is the token; TenantId is stamped by the interceptor. No raw SQL.
/// </summary>
internal sealed class CreateTaskHandler(CrmDbContext db)
    : ICommandHandler<CreateTaskCommand, CreateTaskResponse>
{
    public async Task<CreateTaskResponse> Handle(CreateTaskCommand command, CancellationToken ct)
    {
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

        var task = new CrmTask
        {
            UserId = command.UserId,
            ContactId = command.ContactId,
            DealId = command.DealId,
            Title = command.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description,
            DueAt = command.DueAt,
            Priority = command.Priority,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return new CreateTaskResponse(task.Id);
    }
}
