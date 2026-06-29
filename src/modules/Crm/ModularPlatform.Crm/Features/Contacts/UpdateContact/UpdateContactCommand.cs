using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

public sealed record UpdateContactCommand(
    Guid UserId,
    Guid ContactId,
    string? FullName,
    string? Email,
    string? Phone,
    string? Company,
    string? Position,
    string? Notes,
    string[]? Tags,
    string? Status) : ICommand<ModularPlatform.Crm.Features.Contacts.ContactResponse>;

public sealed record UpdateContactRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? Company,
    string? Position,
    string? Notes,
    string[]? Tags,
    string? Status);
