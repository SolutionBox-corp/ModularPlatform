namespace ModularPlatform.Crm.Features.Contacts;

/// <summary>Shared read DTOs for the Contacts feature. Records (immutable wire shapes).</summary>
public sealed record ContactResponse(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? Company,
    string? Position,
    string? Notes,
    string[] Tags,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ContactListItem(
    Guid Id,
    string FullName,
    string? Email,
    string? Company,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record InteractionResponse(
    Guid Id,
    Guid ContactId,
    string Type,
    DateTimeOffset OccurredAt,
    string? Body);
