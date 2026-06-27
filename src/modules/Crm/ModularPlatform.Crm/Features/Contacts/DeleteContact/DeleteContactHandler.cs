using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.DeleteContact;

/// <summary>
/// Soft-deletes a tracked contact owned by the caller (ISoftDeletable → the global filter then hides it).
/// Foreign/missing id ⇒ 404 (RLS + the WHERE both scope to the owner). No event is published.
/// </summary>
internal sealed class DeleteContactHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteContactCommand, Unit>
{
    public async Task<Unit> Handle(DeleteContactCommand command, CancellationToken ct)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == command.ContactId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");

        contact.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
