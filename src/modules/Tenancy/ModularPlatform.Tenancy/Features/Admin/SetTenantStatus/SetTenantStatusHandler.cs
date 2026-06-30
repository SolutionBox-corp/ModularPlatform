using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Tenancy.Features.Admin.SetTenantStatus;

internal sealed class SetTenantStatusHandler(IDbContextOutbox<TenancyDbContext> outbox)
    : ICommandHandler<SetTenantStatusCommand, SetTenantStatusResponse>
{
    public async Task<SetTenantStatusResponse> Handle(SetTenantStatusCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Status)
            || !Enum.TryParse<TenantStatus>(command.Status.Trim(), ignoreCase: true, out var status)
            || status is not (TenantStatus.Active or TenantStatus.Suspended))
        {
            throw new BusinessRuleException(
                "tenant.status.invalid",
                "Tenant status must be Active or Suspended.");
        }

        var tenant = await outbox.DbContext.Tenants.FirstOrDefaultAsync(t => t.Id == command.TenantId, ct)
            ?? throw new NotFoundException("tenant.not_found", "Workspace not found.");

        tenant.Status = status;

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new SetTenantStatusResponse(command.TenantId, status.ToString());
    }
}
