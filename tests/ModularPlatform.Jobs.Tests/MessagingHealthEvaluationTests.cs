using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Jobs;
using Shouldly;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;

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

    [Fact]
    public void Incoming_pending_above_threshold_warns_separately_from_outgoing_backlog()
    {
        var counts = new PersistedCounts { Incoming = 250, Outgoing = 0, Scheduled = 0, DeadLetter = 0 };

        var result = MessagingHealthEvaluation.Evaluate(counts, stuckThreshold: 100);

        result.IncomingPending.ShouldBe(250);
        result.OutgoingPending.ShouldBe(0);
        result.Warnings.ShouldHaveSingleItem();
        result.Warnings[0].ShouldContain("incoming-pending messages exceed stuck threshold 100");
    }

    [Fact]
    public async Task Job_refreshes_all_platform_messaging_gauge_values_from_store_counts()
    {
        var counts = new PersistedCounts { Incoming = 11, Outgoing = 22, Scheduled = 999, DeadLetter = 3 };
        var store = MessageStoreProxy.Create(counts);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:StuckThreshold"] = "100",
            })
            .Build();

        var jobType = typeof(JobsHostBuilder).Assembly
            .GetType("ModularPlatform.Jobs.MessagingHealthJob", throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(jobType);
        var logger = loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null);
        var alertSink = new RecordingAlertSink();
        var job = Activator.CreateInstance(jobType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: [store, configuration, logger, alertSink], culture: null)!;

        var executeTask = (Task)jobType.GetMethod("Execute")!.Invoke(job, [null!])!;
        await executeTask;

        ReadStaticInt(jobType, "_latestDeadLetters").ShouldBe(3);
        ReadStaticInt(jobType, "_latestIncomingPending").ShouldBe(11);
        ReadStaticInt(jobType, "_latestOutgoingPending").ShouldBe(22);
        alertSink.Evaluations.ShouldHaveSingleItem();
        alertSink.Evaluations[0].Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Job_does_not_route_alerts_when_the_message_store_is_healthy()
    {
        var counts = new PersistedCounts { Incoming = 0, Outgoing = 0, Scheduled = 999, DeadLetter = 0 };
        var store = MessageStoreProxy.Create(counts);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:StuckThreshold"] = "100",
            })
            .Build();

        var jobType = typeof(JobsHostBuilder).Assembly
            .GetType("ModularPlatform.Jobs.MessagingHealthJob", throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(jobType);
        var logger = loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null);
        var alertSink = new RecordingAlertSink();
        var job = Activator.CreateInstance(jobType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: [store, configuration, logger, alertSink], culture: null)!;

        var executeTask = (Task)jobType.GetMethod("Execute")!.Invoke(job, [null!])!;
        await executeTask;

        alertSink.Evaluations.ShouldBeEmpty();
    }

    private static int ReadStaticInt(Type type, string fieldName) =>
        (int)type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private sealed class RecordingAlertSink : IMessagingHealthAlertSink
    {
        public List<MessagingHealthEvaluation.Result> Evaluations { get; } = [];

        public Task NotifyAsync(MessagingHealthEvaluation.Result evaluation, CancellationToken ct)
        {
            Evaluations.Add(evaluation);
            return Task.CompletedTask;
        }
    }

    private class MessageStoreProxy : DispatchProxy
    {
        private IMessageStoreAdmin _admin = null!;

        public static IMessageStore Create(PersistedCounts counts)
        {
            var store = Create<IMessageStore, MessageStoreProxy>();
            ((MessageStoreProxy)(object)store)._admin = AdminProxy.Create(counts);
            return store;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_Admin" => _admin,
                "DisposeAsync" => ValueTask.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }

    private class AdminProxy : DispatchProxy
    {
        private PersistedCounts _counts = null!;

        public static IMessageStoreAdmin Create(PersistedCounts counts)
        {
            var admin = Create<IMessageStoreAdmin, AdminProxy>();
            ((AdminProxy)(object)admin)._counts = counts;
            return admin;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IMessageStoreAdmin.FetchCountsAsync)
                ? Task.FromResult(_counts)
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
