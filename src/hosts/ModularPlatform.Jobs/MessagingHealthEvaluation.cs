using Wolverine.Logging;

namespace ModularPlatform.Jobs;

/// <summary>
/// Pure evaluation of a Wolverine <see cref="PersistedCounts"/> snapshot into the gauge values and the warnings
/// the <see cref="MessagingHealthJob"/> emits. Kept separate from the job so the alerting logic is unit-testable
/// without booting the Jobs host or a message store.
/// <para>
/// CRITICAL: the outbox backlog is <see cref="PersistedCounts.Outgoing"/> (durable outgoing messages not yet
/// dispatched), NOT <see cref="PersistedCounts.Scheduled"/> (messages deliberately scheduled for the future, e.g.
/// saga timeouts). Watching Scheduled both HID a genuinely stuck outbox (it stayed at 0) and raised false alarms
/// whenever long saga timeouts were in flight.
/// </para>
/// </summary>
public static class MessagingHealthEvaluation
{
    public sealed record Result(
        int DeadLetters,
        int IncomingPending,
        int OutgoingPending,
        IReadOnlyList<string> Warnings);

    public static Result Evaluate(PersistedCounts counts, int stuckThreshold)
    {
        var warnings = new List<string>();

        if (counts.DeadLetter > 0)
        {
            warnings.Add(
                $"Messaging health: {counts.DeadLetter} dead-letter(s) found in the Wolverine DLQ — inspect and replay");
        }

        if (counts.Incoming > stuckThreshold)
        {
            warnings.Add(
                $"Messaging health: {counts.Incoming} incoming-pending messages exceed stuck threshold {stuckThreshold}");
        }

        if (counts.Outgoing > stuckThreshold)
        {
            warnings.Add(
                $"Messaging health: {counts.Outgoing} outgoing (outbox-backlog) messages exceed stuck threshold {stuckThreshold}");
        }

        return new Result(counts.DeadLetter, counts.Incoming, counts.Outgoing, warnings);
    }
}
