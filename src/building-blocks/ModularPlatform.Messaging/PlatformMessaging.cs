using ModularPlatform.Abstractions;
using Wolverine;
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
    public static void Configure(WolverineOptions options, string postgresConnectionString, IEnumerable<IModule> modules)
    {
        // Durable transactional inbox/outbox on Postgres — no extra broker infra to start.
        // Swap to RabbitMQ here (one line) when a module is extracted to its own service.
        options.PersistMessagesWithPostgresql(postgresConnectionString);

        // Wrap message handlers in a transaction + outbox automatically (the outbox guarantee).
        options.Policies.AutoApplyTransactions();

        // At-least-once delivery with durable local queues; the inbox UNIQUE(MessageId) dedups duplicates.
        options.Policies.UseDurableLocalQueues();

        foreach (var module in modules)
        {
            module.ConfigureMessaging(options);
        }
    }
}
