using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.PurgeRefreshTokens;

/// <summary>Deletes refresh tokens that expired more than the retention window ago — bounds unbounded growth of the
/// append-rotation table. System/cron command (no per-user scope).</summary>
public sealed record PurgeRefreshTokensCommand : ICommand;
