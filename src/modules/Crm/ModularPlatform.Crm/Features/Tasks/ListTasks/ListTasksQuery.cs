using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.ListTasks;

/// <summary>
/// Owner-scoped, paged task list. <paramref name="Status"/> filters open|done; <paramref name="DueBefore"/> powers
/// the "what to do today" view (tasks due at/before the cutoff). Soonest due first, then newest.
/// </summary>
public sealed record ListTasksQuery(
    Guid UserId,
    string? Status,
    DateTimeOffset? DueBefore,
    Guid? ContactId,
    Guid? DealId,
    int? Page,
    int? PageSize) : IQuery<PagedResponse<ModularPlatform.Crm.Features.Tasks.TaskResponse>>;
