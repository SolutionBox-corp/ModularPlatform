using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Features.Admin.RevokeRole;

/// <summary>Admin operation: remove a role from a user (idempotent — a no-op if not assigned). Pure DB write.</summary>
internal sealed class RevokeRoleHandler(IdentityDbContext db) : ICommandHandler<RevokeRoleCommand, Unit>
{
    public async Task<Unit> Handle(RevokeRoleCommand command, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == command.RoleName, ct);
        if (role is null)
        {
            return Unit.Value;
        }

        var assignment = await db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == command.UserId && ur.RoleId == role.Id, ct);
        if (assignment is not null)
        {
            db.UserRoles.Remove(assignment);
            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
