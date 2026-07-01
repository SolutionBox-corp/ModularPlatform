using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.RevokeMachineToken;

public sealed record RevokeMachineTokenCommand(Guid TenantId, Guid TokenId) : ICommand<RevokeMachineTokenResponse>;

public sealed record RevokeMachineTokenResponse(Guid Id, string Status, DateTimeOffset? RevokedAt);
