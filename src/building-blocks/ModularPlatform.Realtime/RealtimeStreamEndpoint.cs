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
/// </summary>
public static class RealtimeStreamEndpoint
{
    public static void MapRealtimeStream(this IEndpointRouteBuilder app)
    {
        app.MapGet("/realtime/stream", (
                ITenantContext tenant,
                RealtimeConnectionRegistry registry,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                return TypedResults.ServerSentEvents(StreamForUser(userId, registry, ct), eventType: "message");
            })
            .RequireAuthorization()
            .WithTags("Realtime")
            .WithName("RealtimeStream");
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamForUser(
        Guid userId, RealtimeConnectionRegistry registry, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<RealtimeMessage>();
        using var subscription = registry.Subscribe(userId, message =>
        {
            channel.Writer.TryWrite(message);
            return Task.CompletedTask;
        });

        await foreach (var message in channel.Reader.ReadAllAsync(ct))
        {
            yield return new SseItem<string>(message.Json, message.EventType) { EventId = message.Id };
        }
    }
}
