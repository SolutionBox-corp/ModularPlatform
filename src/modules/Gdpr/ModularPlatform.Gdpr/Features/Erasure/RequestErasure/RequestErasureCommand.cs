using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

public sealed record RequestErasureCommand(Guid UserId) : ICommand;

public sealed record RequestErasureRequest(Guid UserId);
