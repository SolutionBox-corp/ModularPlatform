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

        var contact = await (
                from c in db.Contacts.Where(c => c.Id == query.ContactId && c.UserId == query.UserId)
                join company in db.Companies.Where(company => company.UserId == query.UserId)
                    on c.CompanyId equals company.Id into companyGroup
                from company in companyGroup.DefaultIfEmpty()
                select new ContactResponse(
                    c.Id,
                    c.CompanyId,
                    company == null ? null : company.Name,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.Phone,
                    c.Position,
                    c.Notes,
                    c.Tags,
                    c.Status,
                    c.CreatedAt,
                    c.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return contact ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");
    }
}
