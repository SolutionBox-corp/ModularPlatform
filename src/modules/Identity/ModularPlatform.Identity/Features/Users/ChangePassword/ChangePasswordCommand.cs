using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.ChangePassword;

/// <summary>
/// Self-service password change. The user proves possession of the CURRENT password, then sets a new one. Identity
/// comes from the token (<see cref="UserId"/> supplied by the endpoint from <c>ITenantContext.UserId</c>, NEVER the
/// body — Law 10). On success EVERY active refresh token for the user is revoked, so all sessions (including the
/// current one) must re-authenticate with the new password — standard credential-rotation hygiene.
/// </summary>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand;

/// <summary>Wire request — no id (it comes from the token).</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
