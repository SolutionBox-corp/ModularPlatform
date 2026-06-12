using Microsoft.EntityFrameworkCore;
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
        // Idempotent: if the subject's DEK is already shredded the user is already erased — a repeated POST (double
        // click) must NOT re-fan-out erasure across every module. (Re-erasure is safe since each eraser is idempotent;
        // short-circuiting just avoids the needless durable work.)
        if (await outbox.DbContext.SubjectKeys.AnyAsync(k => k.UserId == command.UserId && k.DeletedAt != null, ct))
        {
            return Unit.Value;
        }

        await outbox.PublishAsync(new UserErasureRequested(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            UserId: command.UserId));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return Unit.Value;
    }
}
