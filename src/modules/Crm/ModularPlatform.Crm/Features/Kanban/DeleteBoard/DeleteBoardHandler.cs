using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.DeleteBoard;

/// <summary>Soft-deletes the caller's board + its cards and removes its columns. Foreign/missing ⇒ 404.</summary>
internal sealed class DeleteBoardHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteBoardCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBoardCommand command, CancellationToken ct)
    {
        var board = await db.KanbanBoards
            .FirstOrDefaultAsync(b => b.Id == command.BoardId && b.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.board_not_found", "Board not found.");

        board.DeletedAt = clock.UtcNow;
        await db.KanbanCards.Where(c => c.UserId == command.UserId && c.BoardId == board.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, clock.UtcNow), ct);
        await db.KanbanColumns.Where(c => c.UserId == command.UserId && c.BoardId == board.Id)
            .ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
