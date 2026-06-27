using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Contacts;

namespace ModularPlatform.Crm.Features.Contacts.ListInteractions;

/// <summary>Owner-scoped timeline of a contact's interactions, newest first.</summary>
public sealed record ListInteractionsQuery(Guid UserId, Guid ContactId, int Limit)
    : IQuery<IReadOnlyList<InteractionResponse>>;
