using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.GetContact;

/// <summary><paramref name="UserId"/> (from the token) scopes the read to the owner — defence in depth over RLS.</summary>
public sealed record GetContactQuery(Guid UserId, Guid ContactId)
    : IQuery<ModularPlatform.Crm.Features.Contacts.ContactResponse>;
