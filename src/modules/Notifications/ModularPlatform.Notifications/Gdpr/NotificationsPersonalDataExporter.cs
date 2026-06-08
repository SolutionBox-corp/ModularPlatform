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

        return new Dictionary<string, object?>
        {
            ["notifications"] = notifications,
        };
    }
}
