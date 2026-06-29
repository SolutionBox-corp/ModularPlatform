using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.CreateColumn;

/// <summary>Appends a column to the caller's board (position = current count). Foreign board ⇒ 404.</summary>
internal sealed class CreateColumnHandler(CrmDbContext db)
    : ICommandHandler<CreateColumnCommand, CreateColumnResponse>
{
    public async Task<CreateColumnResponse> Handle(CreateColumnCommand command, CancellationToken ct)
    {
        var owned = await db.KanbanBoards.AnyAsync(b => b.Id == command.BoardId && b.UserId == command.UserId, ct);
        if (!owned)
        {
            throw new NotFoundException("crm.board_not_found", "Board not found.");
        }

        var position = await db.KanbanColumns.CountAsync(c => c.BoardId == command.BoardId, ct);
        var column = new KanbanColumn
        {
            UserId = command.UserId, BoardId = command.BoardId, Name = command.Name.Trim(), Position = position,
        };
        db.KanbanColumns.Add(column);
        await db.SaveChangesAsync(ct);
        return new CreateColumnResponse(column.Id);
    }
}
