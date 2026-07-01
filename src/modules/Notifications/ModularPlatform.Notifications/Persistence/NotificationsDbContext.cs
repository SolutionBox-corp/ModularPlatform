using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Notifications.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Notifications.Persistence;

/// <summary>
/// Notifications module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter and the per-module audit table are applied by the base.
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "notifications";

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
}
