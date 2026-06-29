using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.RequestEmailVerification;

public sealed record RequestEmailVerificationCommand(Guid UserId) : ICommand<RequestEmailVerificationResponse>;

public sealed record RequestEmailVerificationResponse(bool Accepted, bool AlreadyVerified);
