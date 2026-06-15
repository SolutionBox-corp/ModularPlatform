using System.Text.Json;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Messaging;
using ModularPlatform.Marketing.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Marketing.Features.Pulls.TriggerPull;

/// <summary>
/// Long-running accept (canonical 202 pattern): creates a Pending <see cref="DataPull"/> owned by the caller AND
/// publishes the durable <see cref="RunDataPull"/> work message in ONE outbox transaction, then returns its id. The
/// endpoint replies 202; the Worker calls the external API and transitions the pull. The slow call never runs here.
/// </summary>
internal sealed class TriggerPullHandler(IDbContextOutbox<MarketingDbContext> outbox)
    : ICommandHandler<TriggerPullCommand, TriggerPullResponse>
{
    public async Task<TriggerPullResponse> Handle(TriggerPullCommand command, CancellationToken ct)
    {
        var source = Enum.Parse<PullSource>(command.Source, ignoreCase: true);

        var pull = new DataPull
        {
            UserId = command.UserId,
            Source = source,
            Status = PullStatus.Pending,
            ParamsJson = JsonSerializer.Serialize(new { Start = command.StartDate, End = command.EndDate }),
        };
        outbox.DbContext.DataPulls.Add(pull);

        await outbox.PublishAsync(new RunDataPull(pull.Id));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new TriggerPullResponse(pull.Id);
    }
}
