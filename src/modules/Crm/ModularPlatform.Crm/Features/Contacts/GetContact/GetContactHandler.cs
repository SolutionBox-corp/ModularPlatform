using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.GetContact;

/// <summary>Read slice (no-tracking read factory). Owner-scoped by the explicit WHERE and by RLS. Foreign id ⇒ 404.</summary>
internal sealed class GetContactHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetContactQuery, ContactResponse>
{
    public async Task<ContactResponse> Handle(GetContactQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var contact = await db.Contacts
            .Where(c => c.Id == query.ContactId && c.UserId == query.UserId)
            .Select(c => new ContactResponse(
                c.Id, c.CompanyId, c.FullName, c.Email, c.Phone, c.Company, c.Position, c.Notes, c.Tags, c.Status,
                c.CreatedAt, c.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return contact ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");
    }
}
