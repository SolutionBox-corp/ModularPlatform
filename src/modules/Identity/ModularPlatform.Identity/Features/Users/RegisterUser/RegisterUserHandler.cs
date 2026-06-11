using Microsoft.EntityFrameworkCore;
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
    IClock clock)
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

        // Registration runs anonymously (no tenant in context). The tenant REGISTRY is owned by the Tenancy module,
        // so we provision through its port (a separate commit) and assign the user to the returned tenant explicitly
        // — the TenantStampingInterceptor only fills tenant-scoped rows when a tenant is already in context.
        // INTERIM: auto-provision one tenant per registration (preserving today's behavior). The B2B flow —
        // register JOINS the existing tenant resolved from the subdomain — replaces this once the tenant-resolution
        // middleware is wired. The tenant name is a neutral, non-PII identifier (email/display name are encrypted PII
        // and must NOT leak into tenants.Name, which is plaintext at rest and outside the erasure flow).
        var tenantId = await tenantProvisioning.CreateAsync($"tenant-{Guid.CreateVersion7():N}", subdomain: null, ct);

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
            throw new ConflictException("user.email_taken", "This email address is already registered.");
        }

        return new RegisterUserResponse(user.Id);
    }
}
