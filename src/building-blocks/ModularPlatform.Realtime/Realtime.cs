using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using StackExchange.Redis;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ModularPlatform.Operations.Tests")]

namespace ModularPlatform.Realtime;

/// <summary>One server-&gt;client event. Carries an id so SSE Last-Event-ID reconnect can be honored.</summary>
public sealed record RealtimeMessage(string EventType, string Json, string Id);

/// <summary>
/// Port for reading the per-user event replay buffer. Implementations: <see cref="RedisRealtimePublisher"/>
/// (Redis Streams) and <see cref="LocalRealtimePublisher"/> (in-memory ring buffer).
/// </summary>
public interface IRealtimeReplay
{
    /// <summary>
    /// Returns events published after <paramref name="lastEventId"/> (exclusive). An empty or null
    /// <paramref name="lastEventId"/> returns no results (no full-history replay by design — the caller
    /// must have seen at least one event to have a meaningful cursor).
    /// </summary>
    Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(
        Guid userId, string? lastEventId, CancellationToken ct = default);
}

/// <summary>Configuration for the realtime replay buffer.</summary>
public sealed class RealtimeReplayOptions
{
    public const string SectionName = "Realtime:Replay";

    /// <summary>Whether the replay buffer is enabled. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of events retained per user (stream MAXLEN). Default 100.</summary>
    public int MaxEvents { get; set; } = 100;

    /// <summary>TTL (minutes) of the per-user stream key. Default 60.</summary>
    public int TtlMinutes { get; set; } = 60;
}

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
/// instance holding the user's SSE connection. No sticky sessions needed.
/// Also writes to a per-user Redis Stream for Last-Event-ID replay (XADD with MAXLEN cap + TTL refresh).</summary>
internal sealed class RedisRealtimePublisher(IConnectionMultiplexer redis, IOptions<RealtimeReplayOptions> replayOpts)
    : IRealtimePublisher, IRealtimeReplay
{
    public const string UserChannelPrefix = "rt:user:";
    public const string TenantChannelPrefix = "rt:tenant:";
    public const string StreamKeyPrefix = "rts:user:";

    public async Task PublishToUserAsync(Guid userId, string eventType, object payload, CancellationToken ct = default)
    {
        var opts = replayOpts.Value;
        var json = JsonSerializer.Serialize(payload);
        var streamId = RedisValue.Null;

        if (opts.Enabled)
        {
            var streamKey = $"{StreamKeyPrefix}{userId}";
            var db = redis.GetDatabase();
            // XADD with approximate MAXLEN; returns the auto-generated stream id (e.g. "1718000000000-0").
            streamId = await db.StreamAddAsync(
                streamKey,
                [new NameValueEntry("e", eventType), new NameValueEntry("j", json)],
                maxLength: opts.MaxEvents,
                useApproximateMaxLength: true);
            // Refresh the TTL on each publish so idle streams expire.
            await db.KeyExpireAsync(streamKey, TimeSpan.FromMinutes(opts.TtlMinutes));
        }

        var id = streamId.IsNull ? "0" : streamId.ToString();
        await redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"{UserChannelPrefix}{userId}"), Serialize(eventType, json, id));
    }

    public Task PublishToTenantAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"{TenantChannelPrefix}{tenantId}"), Serialize(eventType, JsonSerializer.Serialize(payload), "0"));

    public async Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(
        Guid userId, string? lastEventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lastEventId))
        {
            return [];
        }

        var opts = replayOpts.Value;
        if (!opts.Enabled)
        {
            return [];
        }

        var streamKey = $"{StreamKeyPrefix}{userId}";
        var db = redis.GetDatabase();

        // XRANGE exclusive lower bound: increment the last-seen id by 1 in the sequence number part.
        var exclusiveStart = IncrementStreamId(lastEventId);
        var entries = await db.StreamRangeAsync(streamKey, exclusiveStart, "+");
        if (entries.Length == 0)
        {
            return [];
        }

        var result = new List<RealtimeMessage>(entries.Length);
        foreach (var entry in entries)
        {
            var eventType = (string?)entry["e"] ?? string.Empty;
            var json = (string?)entry["j"] ?? "{}";
            result.Add(new RealtimeMessage(eventType, json, entry.Id.ToString()));
        }

        return result;
    }

    internal static RedisValue Serialize(string eventType, string json, string id) =>
        JsonSerializer.Serialize(new Envelope(eventType, json, id));

    /// <summary>
    /// Increments the sequence part of a Redis stream id (<c>{ms}-{seq}</c>) to produce an exclusive lower
    /// bound for XRANGE. If the id has no sequence, appends <c>-1</c>. Invalid formats fall back to the
    /// original id (XRANGE will return an empty set for an unknown id, which is safe).
    /// </summary>
    private static string IncrementStreamId(string id)
    {
        var dashIndex = id.LastIndexOf('-');
        if (dashIndex < 0)
        {
            return id + "-1";
        }

        var ms = id[..dashIndex];
        var seqPart = id[(dashIndex + 1)..];
        if (ulong.TryParse(seqPart, out var seq))
        {
            return $"{ms}-{seq + 1}";
        }

        return id;
    }

    internal sealed record Envelope(string EventType, string Json, string Id);
}

/// <summary>
/// Single-instance fallback when Redis is not configured: deliver straight to the local registry and
/// maintain a bounded in-memory ring buffer per user for Last-Event-ID replay.
/// </summary>
internal sealed class LocalRealtimePublisher(RealtimeConnectionRegistry registry, IOptions<RealtimeReplayOptions> replayOpts)
    : IRealtimePublisher, IRealtimeReplay
{
    private readonly ConcurrentDictionary<Guid, UserBuffer> _buffers = new();
    private long _counter;

    public Task PublishToUserAsync(Guid userId, string eventType, object payload, CancellationToken ct = default)
    {
        var opts = replayOpts.Value;
        var json = JsonSerializer.Serialize(payload);
        var id = Interlocked.Increment(ref _counter).ToString();

        if (opts.Enabled)
        {
            var buf = _buffers.GetOrAdd(userId, _ => new UserBuffer());
            buf.Push(new RealtimeMessage(eventType, json, id), opts.MaxEvents);
        }

        return registry.DeliverLocal(userId, new RealtimeMessage(eventType, json, id));
    }

    public Task PublishToTenantAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(
        Guid userId, string? lastEventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lastEventId))
        {
            return Task.FromResult<IReadOnlyList<RealtimeMessage>>([]);
        }

        if (!_buffers.TryGetValue(userId, out var buf))
        {
            return Task.FromResult<IReadOnlyList<RealtimeMessage>>([]);
        }

        return Task.FromResult(buf.ReadSince(lastEventId));
    }

    /// <summary>Thread-safe bounded ring buffer with monotonic string ids.</summary>
    private sealed class UserBuffer
    {
        private readonly object _lock = new();
        private readonly List<RealtimeMessage> _items = [];

        public void Push(RealtimeMessage msg, int maxEvents)
        {
            lock (_lock)
            {
                _items.Add(msg);
                // Trim from the front when over capacity.
                if (_items.Count > maxEvents)
                {
                    _items.RemoveAt(0);
                }
            }
        }

        public IReadOnlyList<RealtimeMessage> ReadSince(string lastEventId)
        {
            lock (_lock)
            {
                // Find the index of lastEventId and return everything after it.
                // String ids are monotonic longs: compare numerically for correct ordering.
                if (!long.TryParse(lastEventId, out var lastLong))
                {
                    return [];
                }

                var result = new List<RealtimeMessage>();
                foreach (var msg in _items)
                {
                    if (long.TryParse(msg.Id, out var msgLong) && msgLong > lastLong)
                    {
                        result.Add(msg);
                    }
                }

                return result;
            }
        }
    }
}
