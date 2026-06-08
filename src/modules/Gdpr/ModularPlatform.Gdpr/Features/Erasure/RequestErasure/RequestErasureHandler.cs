using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Contracts;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Abstractions;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

/// <summary>
/// Write slice. Publishes a <see cref="UserErasureRequested"/> integration event through the outbox so
/// every module that holds PII can erase its own slice (each implements IErasePersonalData and subscribes
/// to the event; the Worker runs those handlers, the inbox dedups them). The event is relayed durably in
/// the same transaction as the outbox flush — never fire-and-forget.
/// </summary>
internal sealed class RequestErasureHandler(
    IDbContextOutbox<GdprDbContext> outbox,
    IClock clock)
    : ICommandHandler<RequestErasureCommand>
{
    public async Task<Unit> Handle(RequestErasureCommand command, CancellationToken ct)
    {
        await outbox.PublishAsync(new UserErasureRequested(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            UserId: command.UserId));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return Unit.Value;
    }
}
