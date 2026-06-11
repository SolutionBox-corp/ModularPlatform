using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Realtime;

/// <summary>
/// The browser-facing Server-Sent-Events endpoint: streams this user's realtime events (.NET 10 native SSE).
/// Bridges the registry's push model to an <see cref="IAsyncEnumerable{T}"/> via a channel; the subscription is
/// disposed when the client disconnects (the enumerator is cancelled). Owner-scoped — a user only receives their
/// own events. Multi-instance fan-out is transparent: the Redis subscriber delivers into the same registry.
/// On reconnect the client sends <c>Last-Event-ID</c>; replay events are emitted first (preserving their
/// original ids) before switching to the live stream.
/// </summary>
public static class RealtimeStreamEndpoint
{
    public static void MapRealtimeStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/realtime/stream", (
                ITenantContext tenant,
                RealtimeConnectionRegistry registry,
                IRealtimeReplay replay,
                HttpRequest request,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                // Browser sends Last-Event-ID header on reconnect.
                var lastEventId = request.Headers["Last-Event-ID"].FirstOrDefault();

                return TypedResults.ServerSentEvents(
                    StreamForUser(userId, registry, replay, lastEventId, ct), eventType: "message");
            })
            .RequireAuthorization()
            .WithTags("Realtime")
            .WithName("RealtimeStream");
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamForUser(
        Guid userId,
        RealtimeConnectionRegistry registry,
        IRealtimeReplay replay,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Subscribe first so no live events are lost while we emit the replay. BOUNDED with DropOldest: a stalled or
        // disconnected SSE consumer must NOT grow this buffer without limit (an unbounded channel leaks memory per
        // dead connection). Realtime is best-effort UX smoothing — dropping the oldest unread event under back-pressure
        // is acceptable, and durable facts live in the modules. TryWrite then never blocks and never fails.
        var channel = Channel.CreateBounded<RealtimeMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        using var subscription = registry.Subscribe(userId, message =>
        {
            channel.Writer.TryWrite(message);
            return Task.CompletedTask;
        });

        // Replay buffered events (with their original ids) before serving the live stream.
        if (!string.IsNullOrWhiteSpace(lastEventId))
        {
            var missed = await replay.ReadSinceAsync(userId, lastEventId, ct);
            foreach (var msg in missed)
            {
                yield return new SseItem<string>(msg.Json, msg.EventType) { EventId = msg.Id };
            }
        }

        // Live stream.
        await foreach (var message in channel.Reader.ReadAllAsync(ct))
        {
            yield return new SseItem<string>(message.Json, message.EventType) { EventId = message.Id };
        }
    }
}
