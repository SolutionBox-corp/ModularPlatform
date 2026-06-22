using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Consents.GetConsents;

public sealed record GetConsentsQuery(Guid UserId) : IQuery<IReadOnlyList<ConsentResponse>>;

public sealed record ConsentResponse(Guid Id, string ConsentType, bool Granted, DateTimeOffset RecordedAt, string? PolicyVersion);
