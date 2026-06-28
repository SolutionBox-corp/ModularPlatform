using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
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
    ILogger<RegisterUserHandler> logger,
    IServiceProvider services,
    ITokenIssuer tokenIssuer,
    IOptions<EmailVerificationOptions> emailVerificationOptions)
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

        // JOINING an existing tenant (subdomain signup) is gated by that tenant's RegistrationMode: Open allows,
        // Closed denies, InviteOnly requires a valid single-use invite (consumed here). The gate lives in Tenancy and
        // is resolved optionally — a join can only have been routed here if Tenancy is enabled, so a null gate (or a
        // denied/expired/used invite) fails CLOSED. Checked AFTER the email pre-check so a duplicate email doesn't burn
        // an invite. (Identity never reads the tenant registry directly — Law: cross-module only via a port.)
        if (command.JoinTenantId is { } joinTenantId)
        {
            var gate = services.GetService<ITenantRegistrationGate>();
            if (gate is null || !await gate.TryAcceptJoinAsync(joinTenantId, command.InviteToken, ct))
            {
                throw new ForbiddenException(
                    "registration.not_allowed", "Registration into this workspace is not allowed.");
            }
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
            AcceptedTermsVersion = command.AcceptedTermsVersion?.Trim(),
            AcceptedTermsAt = string.IsNullOrWhiteSpace(command.AcceptedTermsVersion) ? null : clock.UtcNow,
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

        await EmailVerificationIssuer.IssueAsync(
            db,
            outbox,
            tokenIssuer,
            clock,
            emailVerificationOptions.Value,
            user,
            clock.UtcNow,
            ct);

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
