using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.UpdateProfile;

/// <summary>
/// Applies a self-service profile edit. Loads the caller's OWN tracked user (tenant + soft-delete filters apply, so a
/// deleted/erased account 404s), updates the encrypted DisplayName + Locale, and saves. The
/// <c>PersonalDataEncryptionInterceptor</c> re-seals DisplayName on save; xmin + <c>ConcurrencyRetryBehavior</c>
/// serialize concurrent edits. No event is published — no other module consumes a profile change.
/// </summary>
internal sealed class UpdateProfileHandler(IdentityDbContext db)
    : ICommandHandler<UpdateProfileCommand, UserProfileResponse>
{
    public async Task<UserProfileResponse> Handle(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        // Normalize empty/whitespace display name to null so "cleared" reads back as no display name.
        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim();
        user.DisplayName = displayName;
        user.Locale = command.Locale;

        await db.SaveChangesAsync(ct);

        return new UserProfileResponse(user.Id, user.Email, user.DisplayName, user.Locale);
    }
}
