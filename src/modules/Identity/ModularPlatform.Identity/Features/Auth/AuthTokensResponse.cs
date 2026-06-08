namespace ModularPlatform.Identity.Features.Auth;

public sealed record AuthTokensResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
