using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Features.Users.GetProfile;

/// <summary>
/// CANONICAL read slice. Uses <see cref="IReadDbContextFactory{T}"/> (no-tracking, read replica).
/// Queries NEVER mutate, never publish, never open a transaction. Project straight to the response DTO.
/// </summary>
internal sealed class GetProfileHandler(IReadDbContextFactory<IdentityDbContext> readFactory)
    : IQueryHandler<GetProfileQuery, UserProfileResponse>
{
    public async Task<UserProfileResponse> Handle(GetProfileQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var profile = await db.Users
            .Where(u => u.Id == query.UserId)
            .Select(u => new UserProfileResponse(u.Id, u.Email, u.DisplayName, u.Locale))
            .FirstOrDefaultAsync(ct);

        return profile ?? throw new NotFoundException("user.not_found", "User not found.");
    }
}
