using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Entities;
using ModularPlatform.Files.Persistence;

namespace ModularPlatform.Files.Features.Upload;

/// <summary>
/// Stores the bytes in <see cref="IFileStorage"/> under a SERVER-GENERATED opaque key (never the client filename),
/// then persists the metadata row owned by the caller. Pure DB write (no integration event) → scoped DbContext.
/// Bytes go to storage first; the metadata row is the catalog entry the download/list endpoints read (RLS-scoped).
/// </summary>
internal sealed class UploadFileHandler(FilesDbContext db, IFileStorage storage, ILogger<UploadFileHandler> logger)
    : ICommandHandler<UploadFileCommand, UploadFileResponse>
{
    public async Task<UploadFileResponse> Handle(UploadFileCommand command, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        // Opaque, collision-free key derived from the owner + a fresh id — NOT the client filename.
        var storageKey = $"{command.UserId:N}/{id:N}";

        await storage.PutAsync(storageKey, command.Content, command.ContentType, ct);

        var file = new FileObject
        {
            Id = id,
            UserId = command.UserId,
            StorageKey = storageKey,
            FileName = command.FileName,
            ContentType = command.ContentType,
            Size = command.Size,
        };

        db.Files.Add(file);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Compensate: the bytes are already in storage but the metadata row failed to persist — without the row
            // the blob is BOTH unreachable (download needs the row) and leaked. Best-effort delete, then surface the
            // original failure.
            try
            {
                await storage.DeleteAsync(storageKey, ct);
            }
            catch (Exception cleanupError)
            {
                logger.LogError(cleanupError, "Failed to delete orphan blob {StorageKey} after a failed upload.", storageKey);
            }

            throw;
        }

        return new UploadFileResponse(file.Id, file.FileName, file.ContentType, file.Size);
    }
}
