using ModularPlatform.Jobs;
using Shouldly;
using Wolverine.Logging;

namespace ModularPlatform.Jobs.Tests;

/// <summary>
/// The messaging-health alert must watch the OUTBOX BACKLOG (PersistedCounts.Outgoing), not Scheduled
/// (future-dated messages such as saga timeouts). Watching Scheduled hid a genuinely stuck outbox and raised
/// false alarms during long saga timeouts.
/// </summary>
public sealed class MessagingHealthEvaluationTests
{
    [Fact]
    public void A_stuck_outbox_is_reported_via_outgoing_not_scheduled()
    {
        // Outbox is badly backed up; nothing is merely scheduled.
        var counts = new PersistedCounts { Incoming = 0, Outgoing = 500, Scheduled = 0, DeadLetter = 0 };

        var result = MessagingHealthEvaluation.Evaluate(counts, stuckThreshold: 100);

        result.OutgoingPending.ShouldBe(500);
        result.Warnings.ShouldContain(w => w.Contains("outbox-backlog"));
    }

    [Fact]
    public void Scheduled_messages_alone_do_not_raise_a_false_outbox_alarm()
    {
        // Many saga timeouts scheduled for the future, but the outbox is healthy (Outgoing low).
        var counts = new PersistedCounts { Incoming = 0, Outgoing = 0, Scheduled = 10_000, DeadLetter = 0 };

        var result = MessagingHealthEvaluation.Evaluate(counts, stuckThreshold: 100);

        result.OutgoingPending.ShouldBe(0);
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Dead_letters_always_warn()
    {
        var counts = new PersistedCounts { Incoming = 0, Outgoing = 0, Scheduled = 0, DeadLetter = 3 };

        var result = MessagingHealthEvaluation.Evaluate(counts, stuckThreshold: 100);

        result.DeadLetters.ShouldBe(3);
        result.Warnings.ShouldContain(w => w.Contains("dead-letter"));
    }
}
