using JasperFx.CodeGeneration.Model;
using ModularPlatform.Abstractions;
using Wolverine;
using Wolverine.EntityFrameworkCore;
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
    /// <summary>The shared durable Postgres transport queue every bus message flows through.</summary>
    private const string PlatformQueue = "platform";

    /// <param name="listen">
    /// True for a host that CONSUMES messages (the dedicated Worker, and any single-node host that must handle
    /// its own published events — e.g. the Api under <c>Solo</c>/tests). False for a host that only PUBLISHES
    /// (a Balanced Api that offloads to the Worker, the Jobs host, the MigrationService). Routing all messages
    /// through the shared Postgres transport queue is what lets the Worker run handlers OUT of the publishing
    /// process — the durable-local-queue model handled every message in whatever process published it.
    /// </param>
    public static void Configure(
        WolverineOptions options,
        string postgresConnectionString,
        IEnumerable<IModule> modules,
        bool soloMode = false,
        bool listen = false)
    {
        // Postgres-backed durable persistence AND messaging transport (same "wolverine" schema as before — the
        // store + queue tables are auto-provisioned). No extra broker infra. Swap to RabbitMQ here when a module
        // is extracted to its own service.
        options.UsePostgresqlPersistenceAndTransport(postgresConnectionString, "wolverine", "wolverine");

        // EF Core saga persistence + transactional middleware: a Saga type mapped in a module DbContext
        // (registered via AddModuleDbContext → AddDbContextWithWolverineIntegration) is stored through EF.
        // Canonical saga: Billing's CreditPurchaseSaga.
        options.UseEntityFrameworkCoreTransactions();

        // Our message handlers intentionally resolve a scoped service (IDispatcher) to dispatch internal commands.
        // Wolverine 6 makes ServiceLocationPolicy.NotAllowed the default, which SILENTLY skips generating such a
        // handler (the event gets marked Handled but the handler never runs). Allow service location explicitly.
        options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

        if (soloMode)
        {
            options.Durability.Mode = DurabilityMode.Solo;
        }

        // The Postgres queue polls every ScheduledJobPollingTime when idle (Wolverine default 5s) — far too slow
        // for event-driven work (a welcome e-mail / credit grant would lag up to 5s, and the test suite crawled).
        // 1s keeps end-to-end latency low at a negligible polling cost; tune up only if DB poll load matters.
        options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);

        // PII data-minimization for durable envelopes. Integration events carry plaintext personal data that the
        // send path needs (a welcome e-mail's recipient address, a rendered notification body) and that — unlike
        // the DB columns/audit — cannot be crypto-shredded in place inside Wolverine's opaque JSON payloads. So we
        // bound their lifetime instead: PII must not OUTLIVE its operational purpose.
        //  - Handled inbox/outbox envelopes are reaped shortly after success (5m default, set explicitly here).
        //  - Dead-letters expire instead of living forever (the feature is OFF by default → PII would persist
        //    indefinitely). 7 days gives an ops/diagnostics window; recovery of a genuinely lost grant does NOT
        //    depend on the dead-letter (it is reconstructed from live Stripe state by ReconcileStripe). After the
        //    window the row — and its PII — is expunged. (Redis realtime replay is separately TTL-bounded.)
        options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);
        options.Durability.DeadLetterQueueExpirationEnabled = true;
        options.Durability.DeadLetterQueueExpiration = TimeSpan.FromDays(7);

        // Resilience: a transient handler failure retries with growing cooldowns; once exhausted Wolverine moves
        // the message to its durable dead-letter store (inspectable + replayable) instead of losing it. Combined
        // with the inbox UNIQUE(MessageId) this stays effectively exactly-once. Per-external-system drift is
        // handled by a module-specific reconciliation job (Jobs host) — there is no generic reconciler.
        options.Policies.OnException<Exception>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3))
            .Then.MoveToErrorQueue();

        // Wrap message handlers in a transaction + outbox automatically (the outbox guarantee).
        options.Policies.AutoApplyTransactions();

        // Route every bus-published message (integration events, saga messages) to the shared durable queue so a
        // dedicated Worker can consume them; the inbox UNIQUE(MessageId) dedups duplicates (at-least-once).
        options.PublishAllMessages().ToPostgresqlQueue(PlatformQueue);

        // Consumers listen on the same queue. A pure publisher (Balanced Api / Jobs / MigrationService) does not.
        if (listen)
        {
            options.ListenToPostgresqlQueue(PlatformQueue);
        }

        foreach (var module in modules)
        {
            // Discover each module's Wolverine message handlers (integration-event consumers live in module
            // assemblies, not the host). Without this, cross-module events publish but are NEVER consumed.
            options.Discovery.IncludeAssembly(module.GetType().Assembly);
            module.ConfigureMessaging(options);
        }
    }
}
