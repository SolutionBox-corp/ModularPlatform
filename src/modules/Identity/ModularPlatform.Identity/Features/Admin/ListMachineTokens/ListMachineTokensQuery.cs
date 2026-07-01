using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.ListMachineTokens;

public sealed record ListMachineTokensQuery(Guid TenantId) : IQuery<ListMachineTokensResponse>;

public sealed record ListMachineTokensResponse(IReadOnlyList<MachineTokenSummary> Items);

public sealed record MachineTokenSummary(
    Guid Id,
    Guid MachineSubjectId,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);
