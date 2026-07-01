using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;
using ModularPlatform.Tenancy.Services;
using System.Text.Json;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Tenancy.Features.Admin.SetEntitlement;

internal sealed class SetEntitlementHandler(IDbContextOutbox<TenancyDbContext> outbox, IClock clock)
    : ICommandHandler<SetEntitlementCommand, SetEntitlementResponse>
{
    public async Task<SetEntitlementResponse> Handle(SetEntitlementCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var moduleKey = command.ModuleKey.Trim().ToLowerInvariant();
        var tier = string.IsNullOrWhiteSpace(command.Tier) ? null : command.Tier.Trim();
        var limits = NormalizeLimits(command.Limits);

        if (!ProductModuleKeys.IsKnown(moduleKey))
        {
            throw new BusinessRuleException("tenant.module_unknown", "Unknown module key.");
        }

        if (!await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct))
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        var entitlement = await db.TenantEntitlements
            .FirstOrDefaultAsync(e => e.TenantId == command.TenantId && e.ModuleKey == moduleKey, ct);

        if (entitlement is null)
        {
            entitlement = new TenantEntitlement
            {
                TenantId = command.TenantId,
                ModuleKey = moduleKey,
                ValidFrom = clock.UtcNow,
            };
            db.TenantEntitlements.Add(entitlement);
        }

        entitlement.Enabled = command.Enabled;
        entitlement.Tier = tier;
        entitlement.Limits = limits;
        // An explicit admin set is open-ended: clear any stale validity window left from a prior trial, otherwise an old
        // ValidTo would silently re-disable the module again when it elapses (the command carries no window to re-apply).
        entitlement.ValidFrom = clock.UtcNow;
        entitlement.ValidTo = null;

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new SetEntitlementResponse(command.TenantId, moduleKey, command.Enabled, tier, limits);
    }

    private static string? NormalizeLimits(string? limits)
    {
        if (string.IsNullOrWhiteSpace(limits))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(limits);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new BusinessRuleException("tenant.entitlement_limits.invalid", "Limits must be a JSON object.");
            }

            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("tenant.entitlement_limits.invalid", "Limits must be a JSON object.");
        }
    }
}
