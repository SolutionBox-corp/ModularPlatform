using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Features.Users.AcceptTerms;

internal sealed class AcceptTermsHandler(IdentityDbContext db, IClock clock)
    : ICommandHandler<AcceptTermsCommand, UserProfileResponse>
{
    public async Task<UserProfileResponse> Handle(AcceptTermsCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        user.AcceptedTermsVersion = command.TermsVersion.Trim();
        user.AcceptedTermsAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Locale,
            user.EmailConfirmed,
            user.AcceptedTermsVersion,
            user.AcceptedTermsAt);
    }
}
