using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
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
    IClock clock)
    : ICommandHandler<RegisterUserCommand, RegisterUserResponse>
{
    public async Task<RegisterUserResponse> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();

        // Email uniqueness + auth lookups are cross-tenant (the tenant is unknown until authenticated), so they
        // bypass the tenant query filter.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
        {
            throw new ConflictException("user.email_taken", "This email address is already registered.");
        }

        // Registration runs anonymously (no tenant in context), so it provisions a NEW tenant and assigns the
        // user to it explicitly — the TenantStampingInterceptor only fills tenant-scoped rows created later.
        var tenant = new Tenant { Name = command.DisplayName?.Trim() ?? command.Email.Trim(), CreatedAt = clock.UtcNow };
        db.Tenants.Add(tenant);

        var user = new User
        {
            Email = command.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.Hash(command.Password),
            DisplayName = command.DisplayName?.Trim(),
            Locale = "en",
        };

        db.Users.Add(user);
        db.Entry(user).Property<Guid?>("TenantId").CurrentValue = tenant.Id;

        await outbox.PublishAsync(new UserRegisteredIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new RegisterUserResponse(user.Id);
    }
}
