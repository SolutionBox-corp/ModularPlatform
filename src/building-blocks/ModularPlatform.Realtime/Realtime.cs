using System.Collections.Concurrent;
using System.Text.Json;
using ModularPlatform.Abstractions;
using StackExchange.Redis;

namespace ModularPlatform.Realtime;

/// <summary>One server-&gt;client event. Carries an id so SSE Last-Event-ID reconnect can be honored.</summary>
public sealed record RealtimeMessage(string EventType, string Json, string Id);

/// <summary>
/// Per-API-instance registry of live connections. The Redis subscriber (or the local-only publisher) calls
/// <see cref="DeliverLocal"/>; an SSE endpoint calls <see cref="Subscribe"/> and streams what arrives.
/// </summary>
public sealed class RealtimeConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Func<RealtimeMessage, Task>>> _byUser = new();

    public IDisposable Subscribe(Guid userId, Func<RealtimeMessage, Task> onMessage)
    {
        var id = Guid.CreateVersion7();
        var conns = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Func<RealtimeMessage, Task>>());
        conns[id] = onMessage;
        return new Subscription(() =>
        {
            if (_byUser.TryGetValue(userId, out var set))
            {
                set.TryRemove(id, out _);
            }
        });
    }

    public async Task DeliverLocal(Guid userId, RealtimeMessage message)
    {
        if (_byUser.TryGetValue(userId, out var conns))
        {
            foreach (var handler in conns.Values)
            {
                await handler(message);
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>Redis pub/sub fan-out so an event produced on any instance (or the Worker) reaches the
/// instance holding the user's SSE connection. No sticky sessions needed.</summary>
internal sealed class RedisRealtimePublisher(IConnectionMultiplexer redis) : IRealtimePublisher
{
    public const string UserChannelPrefix = "rt:user:";
    public const string TenantChannelPrefix = "rt:tenant:";

    public Task PublishToUserAsync(Guid userId, string eventType, object payload, CancellationToken ct = default) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"{UserChannelPrefix}{userId}"), Serialize(eventType, payload));

    public Task PublishToTenantAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"{TenantChannelPrefix}{tenantId}"), Serialize(eventType, payload));

    internal static RedisValue Serialize(string eventType, object payload) =>
        JsonSerializer.Serialize(new Envelope(eventType, JsonSerializer.Serialize(payload)));

    internal sealed record Envelope(string EventType, string Json);
}

/// <summary>Single-instance fallback when Redis is not configured: deliver straight to the local registry.</summary>
internal sealed class LocalRealtimePublisher(RealtimeConnectionRegistry registry) : IRealtimePublisher
{
    public Task PublishToUserAsync(Guid userId, string eventType, object payload, CancellationToken ct = default) =>
        registry.DeliverLocal(userId, new RealtimeMessage(eventType, JsonSerializer.Serialize(payload), "0"));

    public Task PublishToTenantAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;
}
