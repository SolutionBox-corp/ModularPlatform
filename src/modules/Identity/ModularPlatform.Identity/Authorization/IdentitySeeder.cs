using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Authorization;

/// <summary>The well-known system roles. <see cref="Admin"/> holds every permission and is granted to configured admins.</summary>
internal static class SystemRoles
{
    public const string Admin = "admin";
}

/// <summary>
/// Idempotently seeds the authorization model on startup (after migrations have created the tables): upserts every
/// <see cref="PlatformPermissions"/> into the permissions table, ensures the system <c>admin</c> role exists with
/// ALL permissions, and grants that role to any user whose email is in <c>Identity:Auth:AdminEmails</c>. Safe to
/// re-run and to run on multiple hosts — unique constraints make concurrent duplicate inserts no-ops.
/// </summary>
internal sealed class IdentitySeeder(
    IServiceProvider services,
    IOptions<IdentityAuthOptions> options,
    ILogger<IdentitySeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var blindIndex = scope.ServiceProvider.GetRequiredService<IBlindIndexHasher>();

            await SeedPermissionsAndAdminRoleAsync(db, ct);
            await AssignAdminsAsync(db, blindIndex, options.Value.AdminEmails, ct);
        }
        catch (DbUpdateException ex)
        {
            // Another host seeded concurrently and won a unique-index race — the data is there, so this is benign.
            logger.LogInformation(ex, "Identity authorization seeding skipped a concurrent duplicate.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task SeedPermissionsAndAdminRoleAsync(IdentityDbContext db, CancellationToken ct)
    {
        var existingPermissions = await db.Permissions.ToDictionaryAsync(p => p.Name, ct);
        foreach (var name in PlatformPermissions.All)
        {
            if (!existingPermissions.ContainsKey(name))
            {
                var permission = new Permission { Name = name };
                db.Permissions.Add(permission);
                existingPermissions[name] = permission;
            }
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.Admin, ct);
        if (adminRole is null)
        {
            adminRole = new Role { Name = SystemRoles.Admin, IsSystem = true };
            db.Roles.Add(adminRole);
        }

        await db.SaveChangesAsync(ct); // persist new permissions + role so their Ids are final before linking.

        var linkedPermissionIds = await db.RolePermissions
            .Where(rp => rp.RoleId == adminRole.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);
        var linked = linkedPermissionIds.ToHashSet();

        foreach (var permission in existingPermissions.Values)
        {
            if (!linked.Contains(permission.Id))
            {
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permission.Id });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task AssignAdminsAsync(
        IdentityDbContext db, IBlindIndexHasher blindIndex, string[] adminEmails, CancellationToken ct)
    {
        if (adminEmails.Length == 0)
        {
            return;
        }

        var adminRole = await db.Roles.FirstAsync(r => r.Name == SystemRoles.Admin, ct);
        // Email at rest is ciphertext — match via the keyed blind index over the normalized addresses.
        var hashes = adminEmails.Select(e => blindIndex.Hash(e.Trim().ToUpperInvariant())).ToArray();

        // Users are global authz subjects here — look them up across tenants (this runs as system context).
        var users = await db.Users.IgnoreQueryFilters()
            .Where(u => hashes.Contains(u.EmailHash))
            .Select(u => u.Id)
            .ToListAsync(ct);

        var alreadyAdmin = await db.UserRoles
            .Where(ur => ur.RoleId == adminRole.Id && users.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        var have = alreadyAdmin.ToHashSet();

        foreach (var userId in users.Where(id => !have.Contains(id)))
        {
            db.UserRoles.Add(new UserRole { UserId = userId, RoleId = adminRole.Id });
        }

        await db.SaveChangesAsync(ct);
    }
}
