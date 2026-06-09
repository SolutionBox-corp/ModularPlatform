using JasperFx.CodeGeneration.Model;
using ModularPlatform.Abstractions;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace ModularPlatform.Messaging;

/// <summary>
/// Central Wolverine configuration for every host. The host calls
/// <c>builder.Host.UseWolverine(opts =&gt; PlatformMessaging.Configure(opts, conn, modules))</c>.
/// This is the ONLY place durable messaging is configured — modules contribute via
/// <see cref="IModule.ConfigureMessaging"/>, never by re-configuring transports themselves.
/// </summary>
public static class PlatformMessaging
{
    /// <param name="soloMode">
    /// True for a SINGLE-NODE host (integration tests, local dev, single-instance deploy). In the default
    /// <c>Balanced</c> mode the durability agent that drains durable local queues is leadership-gated — a lone,
    /// short-lived node may never win election, so persisted events sit undelivered and the handler never runs.
    /// <c>Solo</c> starts that agent immediately. Use <c>Balanced</c> (false) only when an Api + dedicated Worker
    /// run as multiple coordinated nodes.
    /// </param>
    public static void Configure(
        WolverineOptions options,
        string postgresConnectionString,
        IEnumerable<IModule> modules,
        bool soloMode = false)
    {
        // Durable transactional inbox/outbox on Postgres — no extra broker infra to start.
        // Swap to RabbitMQ here (one line) when a module is extracted to its own service.
        options.PersistMessagesWithPostgresql(postgresConnectionString);

        // Our message handlers intentionally resolve a scoped service (IDispatcher) to dispatch internal commands.
        // Wolverine 6 makes ServiceLocationPolicy.NotAllowed the default, which SILENTLY skips generating such a
        // handler (the event gets marked Handled but the handler never runs). Allow service location explicitly.
        options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

        if (soloMode)
        {
            options.Durability.Mode = DurabilityMode.Solo;
        }

        // Resilience: a transient handler failure retries with growing cooldowns; once exhausted Wolverine moves
        // the message to its durable dead-letter store (inspectable + replayable) instead of losing it. Combined
        // with the inbox UNIQUE(MessageId) this stays effectively exactly-once. Per-external-system drift is
        // handled by a module-specific reconciliation job (Jobs host) — there is no generic reconciler.
        options.Policies.OnException<Exception>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3))
            .Then.MoveToErrorQueue();

        // Wrap message handlers in a transaction + outbox automatically (the outbox guarantee).
        options.Policies.AutoApplyTransactions();

        // At-least-once delivery with durable local queues; the inbox UNIQUE(MessageId) dedups duplicates.
        options.Policies.UseDurableLocalQueues();

        foreach (var module in modules)
        {
            // Discover each module's Wolverine message handlers (integration-event consumers live in module
            // assemblies, not the host). Without this, cross-module events publish but are NEVER consumed.
            options.Discovery.IncludeAssembly(module.GetType().Assembly);
            module.ConfigureMessaging(options);
        }
    }
}
