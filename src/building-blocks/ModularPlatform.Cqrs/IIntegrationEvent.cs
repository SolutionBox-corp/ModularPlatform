namespace ModularPlatform.Cqrs;

/// <summary>
/// A fact that already happened, published across module boundaries via the transactional outbox
/// and delivered durably by the message bus. Defined in a module's <c>*.Contracts</c> assembly
/// (the only public surface), consumed by handlers in other modules. Must be immutable and
/// carry only IDs + primitive data — never entity graphs.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
