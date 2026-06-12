using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

public sealed record GrantConsentCommand(Guid UserId, string ConsentType) : ICommand<GrantConsentResponse>;

public sealed record GrantConsentResponse(Guid ConsentRecordId);

// No UserId — the subject ALWAYS comes from the token (the endpoint sets it); accepting it from the body would be a
// misleading contract that invites a future IDOR regression.
public sealed record GrantConsentRequest(string ConsentType);
