using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Companies.DeleteCompany;

public sealed record DeleteCompanyCommand(Guid UserId, Guid CompanyId) : ICommand<Unit>;
