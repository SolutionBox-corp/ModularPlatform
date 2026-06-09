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
internal sealed class UploadFileHandler(FilesDbContext db, IFileStorage storage)
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
        await db.SaveChangesAsync(ct);

        return new UploadFileResponse(file.Id, file.FileName, file.ContentType, file.Size);
    }
}
