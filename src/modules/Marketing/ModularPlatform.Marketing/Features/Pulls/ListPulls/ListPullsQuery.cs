using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Pulls.GetPullStatus;

namespace ModularPlatform.Marketing.Features.Pulls.ListPulls;

/// <summary>Paged list of the caller's data pulls, newest first. Reuses <see cref="PullStatusResponse"/> as the item shape.</summary>
public sealed record ListPullsQuery(Guid UserId, PageRequest Page) : IQuery<PagedResponse<PullStatusResponse>>;
