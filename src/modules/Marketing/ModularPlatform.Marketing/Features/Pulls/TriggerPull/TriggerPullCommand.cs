using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Pulls.TriggerPull;

/// <summary>
/// Accepts a marketing-data pull for the calling user. <c>Source</c> is the wire string (e.g. "ga4", "gsc"); the
/// validator checks it maps to a known <see cref="Entities.PullSource"/>. Dates bound the pull window.
/// </summary>
public sealed record TriggerPullCommand(Guid UserId, string Source, DateOnly StartDate, DateOnly EndDate)
    : ICommand<TriggerPullResponse>;

public sealed record TriggerPullResponse(Guid DataPullId);

/// <summary>Wire request. <c>StartDate</c>/<c>EndDate</c> are optional — the endpoint defaults to the last 28 days.</summary>
public sealed record TriggerPullRequest(string Source, DateOnly? StartDate, DateOnly? EndDate);
