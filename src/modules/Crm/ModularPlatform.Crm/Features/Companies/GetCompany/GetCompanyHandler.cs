using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Companies.GetCompany;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; foreign/missing ⇒ 404 (leaks nothing).</summary>
internal sealed class GetCompanyHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetCompanyQuery, CompanyResponse>
{
    public async Task<CompanyResponse> Handle(GetCompanyQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.Companies
            .Where(c => c.Id == query.CompanyId && c.UserId == query.UserId)
            .Select(c => new CompanyResponse(c.Id, c.Name, c.Domain, c.Industry, c.Notes, c.CreatedAt, c.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("crm.company_not_found", "Company not found.");
    }
}
