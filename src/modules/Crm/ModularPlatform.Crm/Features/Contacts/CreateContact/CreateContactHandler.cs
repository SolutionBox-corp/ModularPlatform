using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.CreateContact;

/// <summary>
/// Write slice WITHOUT an event (no other module reacts to a new contact yet): mutate a tracked entity on the
/// scoped write context and SaveChanges. TenantId is stamped by the interceptor (authenticated request → tenant in
/// context); UserId is the token owner from the command. Email is sealed at rest by the encryption interceptor;
/// the keyed blind index is stored for e-mail lookups.
/// </summary>
internal sealed class CreateContactHandler(CrmDbContext db, IBlindIndexHasher blindIndex)
    : ICommandHandler<CreateContactCommand, CreateContactResponse>
{
    public async Task<CreateContactResponse> Handle(CreateContactCommand command, CancellationToken ct)
    {
        if (command.CompanyId is { } companyId
            && !await db.Companies.AnyAsync(c => c.Id == companyId && c.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.company_not_found", "Company not found.");
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();

        var contact = new Contact
        {
            UserId = command.UserId,
            CompanyId = command.CompanyId,
            FullName = command.FullName.Trim(),
            Email = email,
            EmailHash = email is null ? null : blindIndex.Hash(email.ToUpperInvariant()),
            Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim(),
            Company = string.IsNullOrWhiteSpace(command.Company) ? null : command.Company.Trim(),
            Position = string.IsNullOrWhiteSpace(command.Position) ? null : command.Position.Trim(),
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes,
            Tags = command.Tags,
            Status = command.Status,
        };

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return new CreateContactResponse(contact.Id);
    }
}
