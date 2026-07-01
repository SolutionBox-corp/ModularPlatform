using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Notifications.Gdpr;

/// <summary>
/// GDPR data-portability for the Notifications module: returns the subject's in-app notification feed.
/// Read-only via the read factory; the Gdpr module fans these exports into one document.
/// </summary>
internal sealed class NotificationsPersonalDataExporter(IReadDbContextFactory<NotificationsDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Notifications";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var notifications = await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new
            {
                n.Id,
                n.TemplateKey,
                n.Channel,
                n.Title,
                n.Body,
                n.ReadAt,
                n.CreatedAt,
            })
            .ToListAsync(ct);
        var preferences = await db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Channel)
            .Select(p => new
            {
                p.Channel,
                p.Enabled,
                p.UpdatedAt,
                p.CreatedAt,
            })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["notifications"] = notifications,
            ["preferences"] = preferences,
        };
    }
}
