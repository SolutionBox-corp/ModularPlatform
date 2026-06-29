using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.List;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Rename;

/// <summary>
/// Updates ONLY the display <c>FileName</c> for the caller's own file. The blob and its <c>StorageKey</c> are
/// never touched. A foreign <c>FileId</c> (or a file that belongs to another user) results in 404.
/// </summary>
internal sealed class RenameFileHandler(FilesDbContext db)
    : ICommandHandler<RenameFileCommand, FileListItem>
{
    public async Task<FileListItem> Handle(RenameFileCommand command, CancellationToken ct)
    {
        var file = await db.Files
            .Where(f => f.Id == command.FileId && f.UserId == command.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.not_found", "File not found.");

        file.FileName = command.FileName;
        await db.SaveChangesAsync(ct);

        return new FileListItem(file.Id, file.FileName, file.ContentType, file.Size, file.CreatedAt);
    }
}
