using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Tenancy.Entities;
using ModularPlatform.Tenancy.Persistence;
using ModularPlatform.Tenancy.Services;

namespace ModularPlatform.Tenancy.Features.Admin.CreateTenantInvite;

internal sealed class CreateTenantInviteHandler(TenancyDbContext db, IClock clock)
    : ICommandHandler<CreateTenantInviteCommand, CreateTenantInviteResponse>
{
    public async Task<CreateTenantInviteResponse> Handle(CreateTenantInviteCommand command, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct))
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        // 256-bit token; only its hash is persisted (the raw value is returned once, like a refresh token).
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = clock.UtcNow;
        var expiresAt = now.AddDays(command.ExpiresInDays);

        db.TenantInvites.Add(new TenantInvite
        {
            TenantId = command.TenantId,
            TokenHash = TenantRegistrationGate.HashToken(rawToken),
            ExpiresAt = expiresAt,
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // A 256-bit token colliding with an existing one is astronomically unlikely, but never surface it as a 500.
            throw new ConflictException("tenant.invite.collision", "Could not mint a unique invite. Please retry.");
        }

        return new CreateTenantInviteResponse(rawToken, expiresAt);
    }
}
