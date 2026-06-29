using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

/// <summary>
/// Loads the caller's OWN tracked contact (tenant + soft-delete filters apply, so a deleted/foreign row 404s) and
/// applies a PARTIAL patch: a null field is left unchanged; an explicit empty value clears an optional field. Status
/// is never silently reset (a bare {"status":"customer"} no longer wipes the rest of the contact). Recomputes the
/// e-mail blind index when e-mail changes; the encryption interceptor re-seals encrypted columns; xmin +
/// ConcurrencyRetryBehavior serialize concurrent edits. No event is published.
/// </summary>
internal sealed class UpdateContactHandler(CrmDbContext db, IBlindIndexHasher blindIndex)
    : ICommandHandler<UpdateContactCommand, ContactResponse>
{
    public async Task<ContactResponse> Handle(UpdateContactCommand command, CancellationToken ct)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == command.ContactId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");

        if (command.CompanyIdSet)
        {
            if (command.CompanyId is { } companyId
                && !await db.Companies.AnyAsync(c => c.Id == companyId && c.UserId == command.UserId, ct))
            {
                throw new NotFoundException("crm.company_not_found", "Company not found.");
            }

            contact.CompanyId = command.CompanyId;
        }

        if (command.FullName is not null)
        {
            contact.FullName = command.FullName.Trim();
        }

        if (command.Email is not null)
        {
            var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
            contact.Email = email;
            contact.EmailHash = email is null ? null : blindIndex.Hash(email.ToUpperInvariant());
        }

        if (command.Phone is not null)
        {
            contact.Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();
        }

        if (command.Company is not null)
        {
            contact.Company = string.IsNullOrWhiteSpace(command.Company) ? null : command.Company.Trim();
        }

        if (command.Position is not null)
        {
            contact.Position = string.IsNullOrWhiteSpace(command.Position) ? null : command.Position.Trim();
        }

        if (command.Notes is not null)
        {
            contact.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;
        }

        if (command.Tags is not null)
        {
            contact.Tags = command.Tags;
        }

        if (command.Status is not null)
        {
            contact.Status = command.Status;
        }

        await db.SaveChangesAsync(ct);

        return new ContactResponse(
            contact.Id, contact.CompanyId, contact.FullName, contact.Email, contact.Phone, contact.Company, contact.Position,
            contact.Notes, contact.Tags, contact.Status, contact.CreatedAt, contact.UpdatedAt);
    }
}
