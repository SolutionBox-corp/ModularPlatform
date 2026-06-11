using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModularPlatform.Telemetry;
using Quartz;
using Wolverine.Persistence.Durability;

namespace ModularPlatform.Jobs;

/// <summary>
/// Platform-level Quartz job (Jobs host, not a module) that probes the Wolverine durable message store
/// every 5 minutes (configurable via <c>Messaging:HealthCheckCron</c>). Updates OTel observable gauges and
/// emits a structured warning when dead letters or a high pending count are detected.
/// <para>
/// Dead-letter threshold: any count &gt; 0 triggers a warning (a dead-lettered message is a processing failure
/// requiring attention). Pending threshold: configurable via <c>Messaging:StuckThreshold</c> (default 100).
/// </para>
/// NEVER queries Wolverine's internal tables directly — always through <see cref="IMessageStore.Admin"/>.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class MessagingHealthJob(
    IMessageStore messageStore,
    IConfiguration configuration,
    ILogger<MessagingHealthJob> logger) : IJob
{
    // Static backing fields — ObservableGauge uses a pull (callback) model; the job refreshes these on each run.
    private static int _latestDeadLetters;
    private static int _latestIncomingPending;
    private static int _latestOutgoingPending;

    // Registered once at class load; callbacks pull from the static backing fields.
    private static readonly ObservableGauge<int> DeadLetterGauge =
        PlatformMetrics.Meter.CreateObservableGauge(
            "platform.messaging.dead_letters",
            static () => _latestDeadLetters,
            description: "Number of messages currently in the Wolverine dead-letter queue.");

    private static readonly ObservableGauge<int> IncomingPendingGauge =
        PlatformMetrics.Meter.CreateObservableGauge(
            "platform.messaging.incoming_pending",
            static () => _latestIncomingPending,
            description: "Number of durable incoming messages not yet handled by the Worker.");

    private static readonly ObservableGauge<int> OutgoingPendingGauge =
        PlatformMetrics.Meter.CreateObservableGauge(
            "platform.messaging.outgoing_pending",
            static () => _latestOutgoingPending,
            description: "Number of durable outgoing messages not yet dispatched (outbox backlog).");

    public async Task Execute(IJobExecutionContext context)
    {
        // FetchCountsAsync returns Wolverine.Logging.PersistedCounts with properties:
        // Incoming, Scheduled (future-dated, e.g. saga timeouts), Outgoing (outbox backlog), Handled, DeadLetter.
        var counts = await messageStore.Admin.FetchCountsAsync();
        var stuckThreshold = configuration.GetValue<int>("Messaging:StuckThreshold", 100);

        var evaluation = MessagingHealthEvaluation.Evaluate(counts, stuckThreshold);

        // Refresh backing fields so the next OTel collect picks up fresh values.
        Interlocked.Exchange(ref _latestDeadLetters, evaluation.DeadLetters);
        Interlocked.Exchange(ref _latestIncomingPending, evaluation.IncomingPending);
        Interlocked.Exchange(ref _latestOutgoingPending, evaluation.OutgoingPending);

        foreach (var warning in evaluation.Warnings)
        {
            logger.LogWarning("{MessagingHealthWarning}", warning);
        }

        logger.LogInformation(
            "Messaging health check — dead_letters={DeadLetters} incoming_pending={Incoming} outgoing_pending={Outgoing}",
            evaluation.DeadLetters, evaluation.IncomingPending, evaluation.OutgoingPending);
    }
}
