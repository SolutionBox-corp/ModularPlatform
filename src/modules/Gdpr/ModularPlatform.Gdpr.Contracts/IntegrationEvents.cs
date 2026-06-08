using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Contracts;

/// <summary>
/// Published (via the outbox) when a data subject requests erasure of their personal data.
/// Every module that holds PII subscribes to this with a Wolverine handler and erases its own
/// slice — primarily by destroying the subject's encryption key (crypto-shredding) and
/// anonymizing residual non-PII rows that must survive (AML/tax). This is the ONLY way other
/// modules learn that a subject must be erased: never by referencing Gdpr's Core or its DbContext.
/// </summary>
public sealed record UserErasureRequested(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId) : IIntegrationEvent;
