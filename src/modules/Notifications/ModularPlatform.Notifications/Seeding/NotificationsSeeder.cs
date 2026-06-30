using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModularPlatform.Notifications.Entities;
using ModularPlatform.Notifications.Persistence;

namespace ModularPlatform.Notifications.Seeding;

/// <summary>
/// Idempotently seeds the built-in notification templates on startup. Safe to run on multiple
/// concurrent hosts — the UNIQUE(Key,Locale) index on notification_templates turns a concurrent
/// duplicate insert into a no-op (caught as <see cref="DbUpdateException"/>).
/// Templates seeded: <c>welcome</c>, <c>purchase_completed</c> and <c>subscription_past_due</c> (en/cs).
/// </summary>
internal sealed class NotificationsSeeder(
    IServiceProvider services,
    ILogger<NotificationsSeeder> logger)
    : IHostedService
{
    private static readonly IReadOnlyList<(string Key, string Locale, string Subject, string Body)> Templates =
    [
        ("welcome", "en",
            "Welcome to the platform!",
            "Hi {displayName}, welcome aboard."),
        ("welcome", "cs",
            "Vítejte na platformě!",
            "Dobrý den {displayName}, vítáme vás."),
        ("purchase_completed", "en",
            "Purchase completed",
            "Your purchase of {creditAmount} credits is complete."),
        ("purchase_completed", "cs",
            "Nákup dokončen",
            "Váš nákup {creditAmount} kreditů je dokončen."),
        ("subscription_past_due", "en",
            "Subscription payment failed",
            "Your {planKey} subscription payment failed. Please update your payment method."),
        ("subscription_past_due", "cs",
            "Platba předplatného selhala",
            "Platba za předplatné {planKey} selhala. Aktualizujte prosím platební metodu."),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            foreach (var (key, locale, subject, body) in Templates)
            {
                var exists = await db.NotificationTemplates
                    .AnyAsync(t => t.Key == key && t.Locale == locale, ct);

                if (!exists)
                {
                    db.NotificationTemplates.Add(new NotificationTemplate
                    {
                        Key = key,
                        Locale = locale,
                        Subject = subject,
                        Body = body,
                    });
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Another host won a concurrent seed race — the data is there, this is benign.
            logger.LogInformation(ex, "Notifications template seeding skipped a concurrent duplicate.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: Api/Worker can start before migrations finish or while Postgres is temporarily down.
            // Readiness reports the dependency failure; seeding is idempotent and retries on the next boot.
            logger.LogWarning(ex, "Notifications template seeding did not complete; it will retry on the next boot.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
