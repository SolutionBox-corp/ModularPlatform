using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Tasks;

namespace ModularPlatform.Crm.Features.Tasks.ListTaskComments;

public sealed record ListTaskCommentsQuery(Guid UserId, Guid TaskId, int? Page, int? PageSize)
    : IQuery<PagedResponse<TaskCommentResponse>>;
