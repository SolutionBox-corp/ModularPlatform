using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

public sealed record WithdrawConsentCommand(Guid UserId, string ConsentType, string? PolicyVersion = null) : ICommand<WithdrawConsentResponse>;

public sealed record WithdrawConsentResponse(Guid ConsentRecordId);

// No UserId — the subject ALWAYS comes from the token (the endpoint sets it); accepting it from the body would be a
// misleading contract that invites a future IDOR regression.
public sealed record WithdrawConsentRequest(string ConsentType, string? PolicyVersion = null);
