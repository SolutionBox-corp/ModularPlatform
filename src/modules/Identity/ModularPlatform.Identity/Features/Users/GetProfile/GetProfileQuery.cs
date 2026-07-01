using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.GetProfile;

public sealed record GetProfileQuery(Guid UserId) : IQuery<UserProfileResponse>;

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    string Locale,
    bool EmailConfirmed,
    string? AcceptedTermsVersion,
    DateTimeOffset? AcceptedTermsAt);
