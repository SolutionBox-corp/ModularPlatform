using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Delete;

/// <summary>
/// Deletes the blob FIRST, then the metadata row. Order is deliberate: if <see cref="FilesDbContext.SaveChangesAsync"/>
/// fails after the blob is gone the row still exists but points at a missing blob — an orphaned-pointer state. The
/// inverse (row gone, blob leaked) is worse because it leaks storage silently with no recovery path. A second delete
/// of the same id returns 404 (idempotent from the caller's perspective).
/// </summary>
internal sealed class DeleteFileHandler(
    FilesDbContext db,
    IFileStorage storage,
    ILogger<DeleteFileHandler> logger)
    : ICommandHandler<DeleteFileCommand>
{
    public async Task<Unit> Handle(DeleteFileCommand command, CancellationToken ct)
    {
        var file = await db.Files
            .Where(f => f.Id == command.FileId && f.UserId == command.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.not_found", "File not found.");

        // Blob first — see class summary for ordering rationale.
        try
        {
            await storage.DeleteAsync(file.StorageKey, ct);
        }
        catch (Exception blobError)
        {
            logger.LogError(blobError, "Failed to delete blob {StorageKey} for file {FileId}.", file.StorageKey, file.Id);
            throw;
        }

        var links = await db.FileLinks
            .Where(l => l.UserId == command.UserId && l.FileObjectId == command.FileId)
            .ToListAsync(ct);
        db.FileLinks.RemoveRange(links);

        db.Files.Remove(file);
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
