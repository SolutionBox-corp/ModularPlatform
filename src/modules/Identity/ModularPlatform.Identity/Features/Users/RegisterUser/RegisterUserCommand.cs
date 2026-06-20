using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.RegisterUser;

/// <summary>
/// <paramref name="JoinTenantId"/> is the tenant resolved from the request subdomain (B2B: the user JOINS that
/// existing tenant). Null = no subdomain (apex / localhost) ⇒ the legacy self-serve flow provisions a new tenant.
/// Set by the endpoint from the server-trusted resolved tenant, NEVER from the request body (Law 10).
/// </summary>
public sealed record RegisterUserCommand(
    string Email, string Password, string? DisplayName, Guid? JoinTenantId = null, string? InviteToken = null,
    string? AcceptedTermsVersion = null)
    : ICommand<RegisterUserResponse>;

public sealed record RegisterUserResponse(Guid UserId);

public sealed record RegisterUserRequest(
    string Email, string Password, string? DisplayName, string? InviteToken, string? AcceptedTermsVersion = null);
