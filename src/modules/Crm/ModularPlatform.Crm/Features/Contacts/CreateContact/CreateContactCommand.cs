using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.CreateContact;

/// <summary><paramref name="UserId"/> is the owner, set by the endpoint from the token — NEVER the request body (Law 10).</summary>
public sealed record CreateContactCommand(
    Guid UserId,
    Guid? CompanyId,
    string FullName,
    string? Email,
    string? Phone,
    string? Company,
    string? Position,
    string? Notes,
    string[] Tags,
    string Status) : ICommand<CreateContactResponse>;

public sealed record CreateContactResponse(Guid Id);

public sealed record CreateContactRequest(
    Guid? CompanyId,
    string FullName,
    string? Email,
    string? Phone,
    string? Company,
    string? Position,
    string? Notes,
    string[]? Tags,
    string? Status);
