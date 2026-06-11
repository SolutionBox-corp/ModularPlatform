using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Contracts;

/// <summary>
/// Published (via the outbox) when a user finishes registration. Other modules subscribe to this
/// — e.g. Billing creates a credit account, Notifications sends a welcome email. This is the ONLY
/// way other modules learn about new users: never by referencing Identity's Core or its DbContext.
/// </summary>
public sealed record UserRegisteredIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid TenantId,
    string Email,
    string? DisplayName) : IIntegrationEvent;
