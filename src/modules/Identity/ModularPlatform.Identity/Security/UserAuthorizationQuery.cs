using Microsoft.EntityFrameworkCore;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Security;

/// <summary>
/// Loads a user's role names + the union of their roles' permission names, for baking into the access token as
/// <c>role</c> / <c>permission</c> claims. Shared by login + refresh so the token is authorized the same way on
/// both paths. Authorization is a token snapshot — role/permission changes take effect on the next token (≤ the
/// short access-token lifetime).
/// </summary>
internal static class UserAuthorizationQuery
{
    public static async Task<(List<string> Roles, List<string> Permissions)> LoadAsync(
        IdentityDbContext db, Guid userId, CancellationToken ct)
    {
        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
        {
            return ([], []);
        }

        var roles = await db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(ct);

        var permissions = await db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Name)
            .Distinct()
            .ToListAsync(ct);

        return (roles, permissions);
    }
}
