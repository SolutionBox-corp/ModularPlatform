using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;

namespace ModularPlatform.Identity.Features.Users.UpdateProfile;

/// <summary>
/// Self-service profile update. Identity comes from the token (<see cref="UpdateProfileCommand.UserId"/> is supplied
/// by the endpoint from <c>ITenantContext.UserId</c>, NEVER the body — Law 10), so a user can only edit their OWN
/// profile. Mutates the encrypted <c>DisplayName</c> and <c>Locale</c> on the tracked entity (xmin +
/// <c>ConcurrencyRetryBehavior</c> serialize concurrent edits); returns the updated profile.
/// </summary>
public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string Locale)
    : ICommand<UserProfileResponse>;

/// <summary>Wire request — the caller never sends an id (that comes from the token).</summary>
public sealed record UpdateProfileRequest(string? DisplayName, string Locale);
