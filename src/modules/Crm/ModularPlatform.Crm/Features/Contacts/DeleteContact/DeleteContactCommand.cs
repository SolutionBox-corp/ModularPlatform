using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.DeleteContact;

/// <summary>Soft-deletes the caller's own contact. <paramref name="UserId"/> is the token owner (Law 10).</summary>
public sealed record DeleteContactCommand(Guid UserId, Guid ContactId) : ICommand<Unit>;
