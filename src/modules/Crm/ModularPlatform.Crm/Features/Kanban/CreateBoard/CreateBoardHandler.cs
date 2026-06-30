using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.CreateBoard;

/// <summary>Creates a board with three default columns in one transaction. Owner from token; no event.</summary>
internal sealed class CreateBoardHandler(CrmDbContext db)
    : ICommandHandler<CreateBoardCommand, CreateBoardResponse>
{
    private static readonly (string Name, string Group, string Color, bool IsDefault)[] DefaultColumns =
    [
        ("Backlog", KanbanColumnGroups.Backlog, "#64748B", false),
        ("To Do", KanbanColumnGroups.Unstarted, "#64748B", true),
        ("In Progress", KanbanColumnGroups.Started, "#F59E0B", false),
        ("Done", KanbanColumnGroups.Completed, "#22C55E", false),
    ];

    public async Task<CreateBoardResponse> Handle(CreateBoardCommand command, CancellationToken ct)
    {
        var board = new KanbanBoard { UserId = command.UserId, Name = command.Name.Trim() };
        db.KanbanBoards.Add(board);

        for (var i = 0; i < DefaultColumns.Length; i++)
        {
            db.KanbanColumns.Add(new KanbanColumn
            {
                UserId = command.UserId,
                BoardId = board.Id,
                Name = DefaultColumns[i].Name,
                Position = i,
                Group = DefaultColumns[i].Group,
                Color = DefaultColumns[i].Color,
                IsDefault = DefaultColumns[i].IsDefault,
            });
        }

        await db.SaveChangesAsync(ct);
        return new CreateBoardResponse(board.Id);
    }
}
