using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Persistence;

/// <summary>
/// Identity module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter, soft-delete filter and the per-module audit table are applied by the base.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<MachineTokenIssuance> MachineTokenIssuances => Set<MachineTokenIssuance>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
}
