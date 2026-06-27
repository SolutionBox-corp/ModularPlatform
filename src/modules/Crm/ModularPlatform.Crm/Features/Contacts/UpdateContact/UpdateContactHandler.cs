using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

/// <summary>
/// Loads the caller's OWN tracked contact (tenant + soft-delete filters apply, so a deleted/foreign row 404s),
/// updates fields, recomputes the e-mail blind index, and saves. The encryption interceptor re-seals the encrypted
/// columns on save; xmin + ConcurrencyRetryBehavior serialize concurrent edits. No event is published.
/// </summary>
internal sealed class UpdateContactHandler(CrmDbContext db, IBlindIndexHasher blindIndex)
    : ICommandHandler<UpdateContactCommand, ContactResponse>
{
    public async Task<ContactResponse> Handle(UpdateContactCommand command, CancellationToken ct)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == command.ContactId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();

        contact.FullName = command.FullName.Trim();
        contact.Email = email;
        contact.EmailHash = email is null ? null : blindIndex.Hash(email.ToUpperInvariant());
        contact.Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();
        contact.Company = string.IsNullOrWhiteSpace(command.Company) ? null : command.Company.Trim();
        contact.Position = string.IsNullOrWhiteSpace(command.Position) ? null : command.Position.Trim();
        contact.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;
        contact.Tags = command.Tags;
        contact.Status = command.Status;

        await db.SaveChangesAsync(ct);

        return new ContactResponse(
            contact.Id, contact.FullName, contact.Email, contact.Phone, contact.Company, contact.Position,
            contact.Notes, contact.Tags, contact.Status, contact.CreatedAt, contact.UpdatedAt);
    }
}
