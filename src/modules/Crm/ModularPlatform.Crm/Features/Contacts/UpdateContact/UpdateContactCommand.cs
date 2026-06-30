using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Json;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

public sealed record UpdateContactCommand(
    Guid UserId,
    Guid ContactId,
    Guid? CompanyId,
    bool CompanyIdSet,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Position,
    string? Notes,
    string[]? Tags,
    string? Status) : ICommand<ModularPlatform.Crm.Features.Contacts.ContactResponse>;

public sealed record UpdateContactRequest(
    Optional<Guid?> CompanyId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Position,
    string? Notes,
    string[]? Tags,
    string? Status);
