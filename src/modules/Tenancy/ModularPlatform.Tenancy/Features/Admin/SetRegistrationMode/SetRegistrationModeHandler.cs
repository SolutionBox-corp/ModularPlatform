using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Tenancy.Features.Admin.SetRegistrationMode;

internal sealed class SetRegistrationModeHandler(IDbContextOutbox<TenancyDbContext> outbox)
    : ICommandHandler<SetRegistrationModeCommand, SetRegistrationModeResponse>
{
    public async Task<SetRegistrationModeResponse> Handle(SetRegistrationModeCommand command, CancellationToken ct)
    {
        if (!Enum.TryParse<TenantRegistrationMode>(
                command.RegistrationMode.Trim(),
                ignoreCase: true,
                out var registrationMode))
        {
            throw new BusinessRuleException(
                "tenant.registration_mode.invalid",
                "Registration mode must be Open, InviteOnly or Closed.");
        }

        var tenant = await outbox.DbContext.Tenants.FirstOrDefaultAsync(t => t.Id == command.TenantId, ct)
            ?? throw new NotFoundException("tenant.not_found", "Workspace not found.");

        tenant.RegistrationMode = registrationMode;

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new SetRegistrationModeResponse(command.TenantId, registrationMode.ToString());
    }
}
