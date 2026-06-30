using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;

namespace ModularPlatform.Crm.Features.Contacts.ListInteractions;

/// <summary>Owner-scoped timeline of contact/deal interactions, newest first. Paged so a long history is reachable.</summary>
public sealed record ListInteractionsQuery(Guid UserId, Guid? ContactId, Guid? DealId, int? Page, int? PageSize)
    : IQuery<PagedResponse<InteractionResponse>>;
