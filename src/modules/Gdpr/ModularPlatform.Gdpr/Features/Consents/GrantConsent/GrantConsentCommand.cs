using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

public sealed record GrantConsentCommand(Guid UserId, string ConsentType) : ICommand<GrantConsentResponse>;

public sealed record GrantConsentResponse(Guid ConsentRecordId);

public sealed record GrantConsentRequest(Guid UserId, string ConsentType);
