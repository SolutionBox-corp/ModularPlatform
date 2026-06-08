using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthTokensResponse>;

public sealed record RefreshTokenRequest(string RefreshToken);
