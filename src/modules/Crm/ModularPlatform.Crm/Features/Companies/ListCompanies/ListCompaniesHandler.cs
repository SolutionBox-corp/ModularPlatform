using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Companies.ListCompanies;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; newest first; bounded page size.</summary>
internal sealed class ListCompaniesHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListCompaniesQuery, PagedResponse<CompanyListItem>>
{
    public async Task<PagedResponse<CompanyListItem>> Handle(ListCompaniesQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var paging = new PageRequest(query.Page, query.PageSize);

        var filtered = db.Companies.Where(c => c.UserId == query.UserId);

        if (!string.IsNullOrWhiteSpace(query.Industry))
        {
            var industry = query.Industry.Trim();
            filtered = filtered.Where(c => c.Industry == industry);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            filtered = filtered.Where(c => EF.Functions.ILike(c.Name, $"%{name}%"));
        }

        var total = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(c => c.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new CompanyListItem(c.Id, c.Name, c.Domain, c.Industry, c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<CompanyListItem>(items, paging.Page, paging.PageSize, total);
    }
}
