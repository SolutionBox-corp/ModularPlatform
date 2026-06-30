using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Contacts.AddInteraction;

public sealed record AddInteractionCommand(
    Guid UserId,
    Guid ContactId,
    Guid? DealId,
    string Type,
    DateTimeOffset? OccurredAt,
    string? Body) : ICommand<AddInteractionResponse>;

public sealed record AddInteractionResponse(Guid Id);

public sealed record AddInteractionRequest(Guid? DealId, string Type, DateTimeOffset? OccurredAt, string? Body);
