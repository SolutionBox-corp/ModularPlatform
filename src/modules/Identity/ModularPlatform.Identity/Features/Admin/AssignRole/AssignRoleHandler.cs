using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.AssignRole;

/// <summary>
/// Admin operation: grant a role to a user (idempotent). Runs under the admin's principal but assigns across
/// users/tenants, so it bypasses the tenant filter. Pure DB write — injects the context directly. The target
/// user re-authenticates (or refreshes) to pick up the new role/permission claims.
/// </summary>
internal sealed class AssignRoleHandler(IdentityDbContext db) : ICommandHandler<AssignRoleCommand, Unit>
{
    public async Task<Unit> Handle(AssignRoleCommand command, CancellationToken ct)
    {
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == command.UserId, ct))
        {
            throw new NotFoundException("user.not_found", "User not found.");
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == command.RoleName, ct)
            ?? throw new NotFoundException("role.not_found", "Role not found.");

        var alreadyAssigned = await db.UserRoles
            .AnyAsync(ur => ur.UserId == command.UserId && ur.RoleId == role.Id, ct);
        if (!alreadyAssigned)
        {
            db.UserRoles.Add(new UserRole { UserId = command.UserId, RoleId = role.Id });
            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
