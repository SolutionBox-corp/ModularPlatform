using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.ListContacts;

/// <summary>
/// Owner-scoped, paged contact list with optional filters. <paramref name="Status"/> is an exact lifecycle filter;
/// name is encrypted at rest and cannot be filtered by value (a name blind index is a later enhancement);
/// <paramref name="Email"/> matches exactly via the blind-index hash.
/// </summary>
public sealed record ListContactsQuery(
    Guid UserId,
    string? Status,
    string? Email,
    Guid? CompanyId,
    int? Page,
    int? PageSize) : IQuery<PagedResponse<ModularPlatform.Crm.Features.Contacts.ContactListItem>>;
