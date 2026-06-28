using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Gdpr;

/// <summary>
/// Seals legacy vibe-chat message rows that predate live-column encryption. Fresh rows are encrypted by the normal
/// SaveChanges interceptor; this hosted backfill only touches rows whose provider value is not a <c>penc:v2</c>
/// envelope yet.
/// </summary>
internal sealed class MarketingPiiEncryptionBackfill(
    IServiceProvider services,
    ILogger<MarketingPiiEncryptionBackfill> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();

            var pending = await db.VibeMessages.IgnoreQueryFilters()
                .Where(m => !m.Content.StartsWith("penc:v2:")
                    || (m.ToolCallsJson != null && !m.ToolCallsJson.StartsWith("penc:v2:")))
                .ToListAsync(ct);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var message in pending)
            {
                db.Entry(message).Property(m => m.Content).IsModified = true;
                if (message.ToolCallsJson is not null)
                {
                    db.Entry(message).Property(m => m.ToolCallsJson).IsModified = true;
                }
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Marketing PII encryption backfill sealed {Count} legacy vibe message row(s).", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Marketing PII encryption backfill skipped; will retry on next startup.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
