using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Contacts.ListContacts;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS. Newest first; bounded page size.</summary>
internal sealed class ListContactsHandler(
    IReadDbContextFactory<CrmDbContext> readFactory,
    IBlindIndexHasher blindIndex)
    : IQueryHandler<ListContactsQuery, PagedResponse<ContactListItem>>
{
    public async Task<PagedResponse<ContactListItem>> Handle(ListContactsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.Contacts.Where(c => c.UserId == query.UserId);

        if (query.CompanyId is { } companyId)
        {
            filtered = filtered.Where(c => c.CompanyId == companyId);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            filtered = filtered.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var hash = blindIndex.Hash(query.Email.Trim().ToUpperInvariant());
            filtered = filtered.Where(c => c.EmailHash == hash);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(c => c.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new ContactListItem(
                c.Id,
                c.CompanyId,
                db.Companies
                    .Where(company => company.Id == c.CompanyId && company.UserId == query.UserId)
                    .Select(company => company.Name)
                    .FirstOrDefault(),
                c.FirstName,
                c.LastName,
                c.Email,
                c.Status,
                c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<ContactListItem>(items, paging.Page, paging.PageSize, total);
    }
}
