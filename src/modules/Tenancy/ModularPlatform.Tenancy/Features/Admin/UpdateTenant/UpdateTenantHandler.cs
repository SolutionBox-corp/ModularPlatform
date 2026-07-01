using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Contracts;
using ModularPlatform.Tenancy.Persistence;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Tenancy.Features.Admin.UpdateTenant;

internal sealed class UpdateTenantHandler(IDbContextOutbox<TenancyDbContext> outbox, IClock clock)
    : ICommandHandler<UpdateTenantCommand, UpdateTenantResponse>
{
    public async Task<UpdateTenantResponse> Handle(UpdateTenantCommand command, CancellationToken ct)
    {
        var tenant = await outbox.DbContext.Tenants.FirstOrDefaultAsync(t => t.Id == command.TenantId, ct)
            ?? throw new NotFoundException("tenant.not_found", "Workspace not found.");

        tenant.Name = command.Name.Trim();
        tenant.Subdomain = command.Subdomain.Trim().ToLowerInvariant();

        var now = clock.UtcNow;
        await outbox.PublishAsync(new TenantUpdatedIntegrationEvent(
            Guid.CreateVersion7(),
            now,
            tenant.Id,
            tenant.Subdomain,
            tenant.Name));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("tenant.subdomain_taken", "This subdomain is already taken.");
        }

        return new UpdateTenantResponse(tenant.Id, tenant.Name, tenant.Subdomain);
    }
}
