using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ModularPlatform.Web.Sse;

/// <summary>
/// Per-connection SSE buffer. A handler/endpoint opens one of these, returns its
/// <see cref="ReadAllAsync"/> to <c>TypedResults.ServerSentEvents(...)</c> (.NET 10 native), and the
/// realtime publisher pushes <see cref="Enqueue"/> items into it. Events carry an incrementing id so
/// the client's <c>Last-Event-ID</c> reconnect can be honored by the replay buffer.
/// </summary>
public sealed class SseStream<T>
{
    private readonly Channel<SseItem<T>> _channel =
        Channel.CreateBounded<SseItem<T>>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private long _nextId;

    public void Enqueue(string eventType, T payload)
    {
        var item = new SseItem<T>(payload, eventType)
        {
            EventId = Interlocked.Increment(ref _nextId).ToString(),
        };
        _channel.Writer.TryWrite(item);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<SseItem<T>> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        {
            yield return item;
        }
    }
}
