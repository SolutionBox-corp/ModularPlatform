using System.Net;
using Microsoft.Extensions.Options;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Realtime;
using Shouldly;

namespace ModularPlatform.Operations.Tests;

/// <summary>
/// The browser SSE stream: the endpoint is auth-gated and serves <c>text/event-stream</c>, and the registry that
/// backs it delivers a user's events to their subscription. (The full HTTP receive round-trip isn't asserted —
/// TestServer buffers an infinite SSE response — so the delivery half is proven against the registry directly.)
/// </summary>
[Collection("Integration")]
public sealed class RealtimeSseTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Unauthenticated_stream_is_rejected()
    {
        var response = await fixture.Client.GetAsync("/v1/realtime/stream");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // NOTE: an authenticated GET /realtime/stream cannot be asserted here — TestServer buffers the (infinite)
    // SSE response and never surfaces the headers, so ResponseHeadersRead hangs. The 401 above proves the
    // endpoint is mapped + auth-gated; the registry test below proves delivery. The full HTTP streaming
    // round-trip is verified manually / in a real server, not over TestServer.

    [Fact]
    public async Task Registry_delivers_an_event_only_to_the_owning_user()
    {
        var registry = new RealtimeConnectionRegistry();
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        RealtimeMessage? received = null;
        using var subscription = registry.Subscribe(alice, message =>
        {
            received = message;
            return Task.CompletedTask;
        });

        await registry.DeliverLocal(bob, new RealtimeMessage("other", "{}", "1"));
        received.ShouldBeNull(); // Bob's event must not reach Alice's subscription.

        await registry.DeliverLocal(alice, new RealtimeMessage("notification", "{\"hello\":\"world\"}", "2"));
        received.ShouldNotBeNull();
        received!.EventType.ShouldBe("notification");
    }

    [Fact]
    public async Task Registry_isolates_a_throwing_connection_from_other_connections()
    {
        var registry = new RealtimeConnectionRegistry();
        var userId = Guid.CreateVersion7();

        using var throwing = registry.Subscribe(userId, _ => throw new InvalidOperationException("dead tab"));

        RealtimeMessage? received = null;
        using var healthy = registry.Subscribe(userId, message =>
        {
            received = message;
            return Task.CompletedTask;
        });

        await registry.DeliverLocal(userId, new RealtimeMessage("notification", "{\"ok\":true}", "1"));

        received.ShouldNotBeNull();
        received!.EventType.ShouldBe("notification");
    }

    [Fact]
    public async Task Local_tenant_broadcast_fails_loud_instead_of_silently_dropping_events()
    {
        var publisher = new LocalRealtimePublisher(
            new RealtimeConnectionRegistry(),
            Options.Create(new RealtimeReplayOptions()));

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => publisher.PublishToTenantAsync(Guid.CreateVersion7(), "tenant-event", new { }));

        ex.Message.ShouldContain("tenant broadcast is not yet wired");
    }

    [Fact]
    public async Task Sse_live_buffer_drops_oldest_events_under_back_pressure()
    {
        var channel = RealtimeStreamEndpoint.CreateLiveBuffer(capacity: 2);

        channel.Writer.TryWrite(new RealtimeMessage("one", "{}", "1")).ShouldBeTrue();
        channel.Writer.TryWrite(new RealtimeMessage("two", "{}", "2")).ShouldBeTrue();
        channel.Writer.TryWrite(new RealtimeMessage("three", "{}", "3")).ShouldBeTrue();
        channel.Writer.Complete();

        var retained = new List<RealtimeMessage>();
        await foreach (var message in channel.Reader.ReadAllAsync())
        {
            retained.Add(message);
        }

        retained.Select(x => x.Id).ShouldBe(["2", "3"]);
    }
}
