using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;

namespace ModularPlatform.Identity.Authorization;

/// <summary>
/// One-time, idempotent migration of PRE-ENCRYPTION user rows: any row whose <c>EmailHash</c> is still empty
/// (the migration default) gets its blind index computed and its <c>[Encrypted]</c> columns sealed — simply by
/// marking them modified and saving through the standard pipeline (audit + encryption interceptors). Fresh
/// databases no-op instantly. Runs on every host after migrations; failures are logged and retried on the next
/// boot rather than crashing the host (e.g. the Worker booting before the MigrationService finished).
/// </summary>
internal sealed class PiiEncryptionBackfill(IServiceProvider services, ILogger<PiiEncryptionBackfill> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var blindIndex = scope.ServiceProvider.GetRequiredService<IBlindIndexHasher>();

            var pending = await db.Users.IgnoreQueryFilters()
                .Where(u => u.EmailHash == string.Empty)
                .ToListAsync(ct);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var user in pending)
            {
                user.EmailHash = blindIndex.Hash(user.Email.Trim().ToUpperInvariant());
                // Touch the encrypted columns so the encryption interceptor seals the legacy plaintext.
                db.Entry(user).Property(u => u.Email).IsModified = true;
                if (user.DisplayName is not null)
                {
                    db.Entry(user).Property(u => u.DisplayName).IsModified = true;
                }
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("PII encryption backfill sealed {Count} legacy user row(s).", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Schema not migrated yet, or a concurrent host already backfilled — retry on the next boot.
            logger.LogWarning(ex, "PII encryption backfill skipped; will retry on next startup.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
