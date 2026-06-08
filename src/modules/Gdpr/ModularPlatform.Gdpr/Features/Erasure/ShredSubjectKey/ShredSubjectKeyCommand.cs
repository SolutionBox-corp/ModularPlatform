using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Erasure.ShredSubjectKey;

/// <summary>
/// Internal command — there is no endpoint and it never crosses the assembly boundary. Dispatched only from
/// <see cref="Messaging.UserErasureRequestedHandler"/> as the final, authoritative step of GDPR erasure.
/// </summary>
internal sealed record ShredSubjectKeyCommand(Guid UserId) : ICommand;
