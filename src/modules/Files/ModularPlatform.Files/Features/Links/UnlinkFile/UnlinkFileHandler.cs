using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Links.UnlinkFile;

internal sealed class UnlinkFileHandler(FilesDbContext db) : ICommandHandler<UnlinkFileCommand>
{
    public async Task<Unit> Handle(UnlinkFileCommand command, CancellationToken ct)
    {
        var link = await db.FileLinks
            .Where(l => l.Id == command.LinkId && l.UserId == command.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.link_not_found", "File link not found.");

        db.FileLinks.Remove(link);
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
