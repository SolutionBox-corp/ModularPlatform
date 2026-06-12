using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Identity.Features.Users.RegisterUser;

/// <summary>
/// CANONICAL write slice. Uses Wolverine's <see cref="IDbContextOutbox{T}"/>: the new user AND the
/// <see cref="UserRegisteredIntegrationEvent"/> are committed in ONE transaction, then the event is
/// relayed durably to other modules. Never publish before SaveChanges; never publish fire-and-forget.
/// </summary>
internal sealed class RegisterUserHandler(
    IDbContextOutbox<IdentityDbContext> outbox,
    IPasswordHasher passwordHasher,
    IBlindIndexHasher blindIndex,
    ITenantProvisioning tenantProvisioning,
    IClock clock,
    ILogger<RegisterUserHandler> logger)
    : ICommandHandler<RegisterUserCommand, RegisterUserResponse>
{
    public async Task<RegisterUserResponse> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        // Email at rest is ciphertext — uniqueness + lookups go through the keyed blind index.
        var emailHash = blindIndex.Hash(command.Email.Trim().ToUpperInvariant());

        // Email uniqueness + auth lookups are cross-tenant (the tenant is unknown until authenticated), so they
        // bypass the tenant query filter.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.EmailHash == emailHash, ct))
        {
            throw new ConflictException("user.email_taken", "This email address is already registered.");
        }

        // Registration runs anonymously (no tenant in context). The tenant REGISTRY is owned by the Tenancy module.
        // B2B: on a tenant subdomain the user JOINS that existing tenant (resolved server-side, passed as JoinTenantId).
        // No subdomain (apex / localhost) ⇒ the self-serve "create workspace" flow provisions a NEW tenant via the port.
        // Either way the user is assigned to the tenant EXPLICITLY (the interceptor only fills rows when a tenant is in
        // context). A provisioned tenant's name is a neutral, non-PII identifier (email/display name are encrypted PII).
        // Self-serve path provisions a NEW tenant; remember it so a failed user-save can COMPENSATE (delete the orphan).
        var provisionedTenantId = command.JoinTenantId is null
            ? await tenantProvisioning.CreateAsync($"tenant-{Guid.CreateVersion7():N}", subdomain: null, ct)
            : (Guid?)null;
        var tenantId = command.JoinTenantId ?? provisionedTenantId!.Value;

        var user = new User
        {
            Email = command.Email.Trim(),
            EmailHash = emailHash,
            PasswordHash = passwordHasher.Hash(command.Password),
            DisplayName = command.DisplayName?.Trim(),
            Locale = "en",
        };

        db.Users.Add(user);
        db.Entry(user).Property<Guid?>("TenantId").CurrentValue = tenantId;

        await outbox.PublishAsync(new UserRegisteredIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            UserId: user.Id,
            TenantId: tenantId,
            Email: user.Email,
            DisplayName: user.DisplayName));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two concurrent registrations raced past the pre-check — the UNIQUE(EmailHash) index is the final guard
            // (Law 2 idiom). Narrowed to the unique-violation (23505) so an UNRELATED persistence failure (a future
            // NOT NULL / length / transient fault) is NOT mislabelled as "email already registered" — it surfaces.
            // The self-serve path already committed a fresh tenant in a separate transaction; compensate so the lost
            // race doesn't leak an orphan, owner-less tenant. Best-effort — a cleanup failure is logged, not masked.
            if (provisionedTenantId is { } orphanTenantId)
            {
                try
                {
                    await tenantProvisioning.DeleteAsync(orphanTenantId, ct);
                }
                catch (Exception cleanupError)
                {
                    logger.LogError(cleanupError,
                        "Failed to clean up orphan tenant {TenantId} after a lost registration race.", orphanTenantId);
                }
            }

            throw new ConflictException("user.email_taken", "This email address is already registered.");
        }

        return new RegisterUserResponse(user.Id);
    }
}
