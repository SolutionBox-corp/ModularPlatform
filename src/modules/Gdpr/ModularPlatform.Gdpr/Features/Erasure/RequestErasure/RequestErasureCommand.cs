using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

public sealed record RequestErasureCommand(Guid UserId) : ICommand;

// (No wire request record: the endpoint takes no body — the subject is ALWAYS the authenticated user from the token.
// A request carrying a UserId would be a misleading IDOR-shaped contract.)
