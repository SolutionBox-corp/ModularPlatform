using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Files.Gdpr;

/// <summary>
/// GDPR data-portability for Files: returns the subject's file inventory (metadata only — id, original filename,
/// content-type, size, upload time). Read-only via the read factory; the Gdpr module fans these into one export
/// document. The bytes themselves are not embedded (they are retrievable via the download endpoint while the
/// account exists).
/// </summary>
internal sealed class FilesPersonalDataExporter(IReadDbContextFactory<FilesDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Files";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var files = await db.Files
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.ContentType,
                f.Size,
                f.CreatedAt,
            })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["files"] = files,
        };
    }
}
