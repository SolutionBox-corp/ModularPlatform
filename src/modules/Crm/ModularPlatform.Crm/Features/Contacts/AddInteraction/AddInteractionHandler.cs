using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.AddInteraction;

/// <summary>
/// Logs an interaction against the caller's own contact. Verifies the contact exists and is owned (foreign/missing
/// ⇒ 404) before inserting. <c>OccurredAt</c> defaults to now (UTC via IClock). No event is published.
/// </summary>
internal sealed class AddInteractionHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<AddInteractionCommand, AddInteractionResponse>
{
    public async Task<AddInteractionResponse> Handle(AddInteractionCommand command, CancellationToken ct)
    {
        var contactExists = await db.Contacts
            .AnyAsync(c => c.Id == command.ContactId && c.UserId == command.UserId, ct);
        if (!contactExists)
        {
            throw new NotFoundException("crm.contact_not_found", "Contact not found.");
        }

        var interaction = new ContactInteraction
        {
            UserId = command.UserId,
            ContactId = command.ContactId,
            Type = command.Type,
            OccurredAt = command.OccurredAt ?? clock.UtcNow,
            Body = string.IsNullOrWhiteSpace(command.Body) ? null : command.Body,
        };

        db.ContactInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);

        return new AddInteractionResponse(interaction.Id);
    }
}
