using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.GetUserDetail;

/// <summary>
/// Tenant-admin read for the role manager: a user's profile + their CURRENT role names. The user read deliberately
/// keeps the normal tenant + soft-delete query filters, so <c>identity.manage_roles</c> is not a platform-wide
/// directory permission. <c>Email</c>/<c>DisplayName</c> are [Encrypted]: the read model converter decrypts them on
/// projection (shredded -> <c>[erased]</c>). Roles are resolved by joining <c>user_roles</c> to <c>roles</c> by Id
/// (no navigation).
/// </summary>
internal sealed class GetUserDetailHandler(IReadDbContextFactory<IdentityDbContext> readFactory, IClock clock)
    : IQueryHandler<GetUserDetailQuery, UserDetailResponse>
{
    public async Task<UserDetailResponse> Handle(GetUserDetailQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var user = await db.Users
            .Where(u => u.Id == query.UserId)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.LockoutEndUtc, u.CreatedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == query.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .OrderBy(name => name)
            .ToListAsync(ct);

        var isLocked = user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > clock.UtcNow;

        return new UserDetailResponse(user.Id, user.Email, user.DisplayName, roles, isLocked, user.CreatedAt);
    }
}
