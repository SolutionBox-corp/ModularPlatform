using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Companies.GetCompany;

public sealed record GetCompanyQuery(Guid UserId, Guid CompanyId)
    : IQuery<ModularPlatform.Crm.Features.Companies.CompanyResponse>;
