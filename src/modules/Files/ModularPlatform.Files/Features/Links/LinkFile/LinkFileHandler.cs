using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Entities;
using ModularPlatform.Files.Contracts;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Web;
using Npgsql;

namespace ModularPlatform.Files.Features.Links.LinkFile;

internal sealed class LinkFileHandler(FilesDbContext db)
    : ICommandHandler<LinkFileToOwnerCommand, FileLinkItem>
{
    public async Task<FileLinkItem> Handle(LinkFileToOwnerCommand command, CancellationToken ct)
    {
        var file = await db.Files
            .Where(f => f.Id == command.FileObjectId && f.UserId == command.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.not_found", "File not found.");

        var existing = await FindExistingAsync(command, ct);
        if (existing is not null)
        {
            return ToItem(existing, file);
        }

        var link = new FileLink
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId,
            FileObjectId = file.Id,
            OwnerType = command.OwnerType,
            OwnerId = command.OwnerId,
        };

        db.FileLinks.Add(link);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var raced = await FindExistingAsync(command, ct);
            if (raced is null)
            {
                throw;
            }

            return ToItem(raced, file);
        }

        return ToItem(link, file);
    }

    private Task<FileLink?> FindExistingAsync(LinkFileToOwnerCommand command, CancellationToken ct) =>
        db.FileLinks
            .Where(l => l.UserId == command.UserId
                && l.OwnerType == command.OwnerType
                && l.OwnerId == command.OwnerId
                && l.FileObjectId == command.FileObjectId)
            .FirstOrDefaultAsync(ct);

    private static FileLinkItem ToItem(FileLink link, FileObject file) =>
        new(link.Id, file.Id, link.OwnerType, link.OwnerId, file.FileName, file.ContentType, file.Size, link.CreatedAt);
}
