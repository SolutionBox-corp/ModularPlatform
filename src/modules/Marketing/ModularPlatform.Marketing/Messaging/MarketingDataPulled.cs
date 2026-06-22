using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Analyses.AnalyzeMarketingData;

namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Durable signal that a pull completed and is ready for AI analysis (published by the pull worker via the outbox,
/// consumed by the analysis worker). Intra-module — not a cross-module integration contract.
/// </summary>
public sealed record MarketingDataPulled(Guid DataPullId, Guid UserId, string Source);

/// <summary>
/// Thin PUBLIC Worker shell (Wolverine-discovered, inbox-deduped) that dispatches the internal analysis command —
/// it takes only public types so the module's Core stays internal. The analysis work lives in the command handler.
/// </summary>
public sealed class MarketingDataPulledHandler
{
    public Task Handle(MarketingDataPulled message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new AnalyzeMarketingDataCommand(message.DataPullId, message.UserId, message.Source), ct);
}
