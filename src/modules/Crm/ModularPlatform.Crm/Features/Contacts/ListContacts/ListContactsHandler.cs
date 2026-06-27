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
    : IQueryHandler<ListContactsQuery, ContactsPageResponse>
{
    public async Task<ContactsPageResponse> Handle(ListContactsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

        var filtered = db.Contacts.Where(c => c.UserId == query.UserId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            filtered = filtered.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Company))
        {
            var company = query.Company.Trim();
            filtered = filtered.Where(c => c.Company != null && EF.Functions.ILike(c.Company, $"%{company}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var hash = blindIndex.Hash(query.Email.Trim().ToUpperInvariant());
            filtered = filtered.Where(c => c.EmailHash == hash);
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(c => c.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(c => new ContactListItem(c.Id, c.FullName, c.Email, c.Company, c.Status, c.CreatedAt))
            .ToListAsync(ct);

        return new ContactsPageResponse(items, total, limit, offset);
    }
}
