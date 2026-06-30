namespace ModularPlatform.Crm.Features.Contacts;

/// <summary>Shared read DTOs for the Contacts feature. Records (immutable wire shapes).</summary>
public sealed record ContactResponse(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Position,
    string? Notes,
    string[] Tags,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ContactListItem(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string FirstName,
    string LastName,
    string? Email,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record InteractionResponse(
    Guid Id,
    Guid ContactId,
    string Type,
    DateTimeOffset OccurredAt,
    string? Body);
