using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

public sealed record WithdrawConsentCommand(Guid UserId, string ConsentType) : ICommand<WithdrawConsentResponse>;

public sealed record WithdrawConsentResponse(Guid ConsentRecordId);

public sealed record WithdrawConsentRequest(Guid UserId, string ConsentType);
