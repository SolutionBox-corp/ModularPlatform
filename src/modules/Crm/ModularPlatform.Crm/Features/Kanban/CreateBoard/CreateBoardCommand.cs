using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.CreateBoard;

/// <summary>Creates a board and seeds default columns (To Do / In Progress / Done). Owner from token.</summary>
public sealed record CreateBoardCommand(Guid UserId, string Name) : ICommand<CreateBoardResponse>;

public sealed record CreateBoardResponse(Guid Id);

public sealed record CreateBoardRequest(string Name);
