using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Files.Persistence;

namespace ModularPlatform.Files.Gdpr;

/// <summary>
/// GDPR erasure port for Files. A user's uploaded files (the bytes AND the original client filename) are personal
/// data with NO append-only retention requirement — so they are DELETED outright, not anonymized in place like the
/// notification feed or retained like the financial ledger. Each blob is removed from <see cref="IFileStorage"/>
/// and then the metadata rows are dropped.
/// <para>
/// Idempotent: a re-run (the erasure fan-out is multi-transaction and may retry) finds no rows and does nothing;
/// <see cref="IFileStorage.DeleteAsync"/> is a no-op for an already-removed blob. Runs in the Worker's system
/// context (no tenant), so the tenant query filter does not restrict the match. EF / LINQ only.
/// </para>
/// </summary>
internal sealed class FilesPersonalDataEraser(FilesDbContext db, IFileStorage storage) : IErasePersonalData
{
    public string ModuleName => "Files";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var keys = await db.Files
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.FileName)
            .ThenBy(f => f.StorageKey)
            .Select(f => f.StorageKey)
            .ToListAsync(ct);

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, ct);
        }

        await db.FileLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Files.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
