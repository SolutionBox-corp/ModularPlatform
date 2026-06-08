using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.RegisterUser;

public sealed record RegisterUserCommand(string Email, string Password, string? DisplayName)
    : ICommand<RegisterUserResponse>;

public sealed record RegisterUserResponse(Guid UserId);

public sealed record RegisterUserRequest(string Email, string Password, string? DisplayName);
