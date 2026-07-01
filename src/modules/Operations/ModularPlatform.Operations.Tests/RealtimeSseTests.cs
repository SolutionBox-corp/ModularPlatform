using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Realtime;
using Shouldly;

namespace ModularPlatform.Operations.Tests;

/// <summary>
/// The browser SSE stream: the endpoint is auth-gated and serves <c>text/event-stream</c>, and the registry that
/// backs it delivers a user's events to their subscription. The full HTTP receive path uses Kestrel because
/// TestServer buffers an infinite SSE response.
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

    [Fact]
    public async Task Authenticated_kestrel_sse_stream_receives_a_published_user_event()
    {
        using var liveHost = fixture.CreateHost(("RunMigrationsAtStartup", "false"));
        liveHost.UseKestrel(0);
        liveHost.StartServer();
        using var client = liveHost.CreateClient();

        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(
            $"sse-{Guid.CreateVersion7():N}@example.test",
            "Password123!");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var publisher = liveHost.Services.GetRequiredService<IRealtimePublisher>();
        await publisher.PublishToUserAsync(userId, "notification", new { phase = "replay" }, cts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/realtime/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Last-Event-ID", "0");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        var replayFrame = await ReadSseFrameMatchingAsync(
            reader,
            data => data.Contains("\"phase\":\"replay\"", StringComparison.Ordinal),
            cts.Token);
        replayFrame["event"].ShouldBe("notification");
        replayFrame["id"].ShouldNotBeNullOrWhiteSpace();
        replayFrame["data"].ShouldBe("{\"phase\":\"replay\"}");

        await publisher.PublishToUserAsync(userId, "notification", new { phase = "live" }, cts.Token);

        var liveFrame = await ReadSseFrameMatchingAsync(
            reader,
            data => data.Contains("\"phase\":\"live\"", StringComparison.Ordinal),
            cts.Token);
        liveFrame["event"].ShouldBe("notification");
        liveFrame["id"].ShouldNotBeNullOrWhiteSpace();
        liveFrame["id"].ShouldNotBe(replayFrame["id"]);
        liveFrame["data"].ShouldBe("{\"phase\":\"live\"}");
    }

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

    [Fact]
    public async Task Sse_stream_yields_live_events_after_subscribing_before_replay()
    {
        var registry = new RealtimeConnectionRegistry();
        var userId = Guid.CreateVersion7();
        var replay = new SignalingEmptyReplay();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = RealtimeStreamEndpoint
            .StreamForUser(userId, registry, replay, lastEventId: "0", cts.Token)
            .GetAsyncEnumerator(cts.Token);

        try
        {
            var next = stream.MoveNextAsync().AsTask();
            await replay.ReadStarted.Task.WaitAsync(cts.Token);

            await registry.DeliverLocal(userId, new RealtimeMessage("notification", "{\"ok\":true}", "1"));

            (await next.WaitAsync(cts.Token)).ShouldBeTrue();
            stream.Current.EventId.ShouldBe("1");
            stream.Current.EventType.ShouldBe("notification");
            stream.Current.Data.ShouldBe("{\"ok\":true}");
        }
        finally
        {
            await cts.CancelAsync();
            await stream.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sse_stream_suppresses_replay_live_duplicate_event_ids()
    {
        var registry = new RealtimeConnectionRegistry();
        var userId = Guid.CreateVersion7();
        var duplicate = new RealtimeMessage("notification", "{\"id\":2}", "2");
        var next = new RealtimeMessage("notification", "{\"id\":3}", "3");
        var replay = new ReplayThatAlsoDeliversLive(registry, userId, duplicate, next);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var emitted = new List<string?>();

        await foreach (var item in RealtimeStreamEndpoint
            .StreamForUser(userId, registry, replay, lastEventId: "1", cts.Token)
            .WithCancellation(cts.Token))
        {
            emitted.Add(item.EventId);
            if (emitted.Count == 2)
            {
                break;
            }
        }

        emitted.ShouldBe(["2", "3"]);
    }

    private sealed class SignalingEmptyReplay : IRealtimeReplay
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(
            Guid replayUserId,
            string? lastEventId,
            CancellationToken ct = default)
        {
            lastEventId.ShouldBe("0");
            ReadStarted.TrySetResult();
            return Task.FromResult<IReadOnlyList<RealtimeMessage>>([]);
        }
    }

    private sealed class ReplayThatAlsoDeliversLive(
        RealtimeConnectionRegistry registry,
        Guid userId,
        RealtimeMessage duplicate,
        RealtimeMessage next) : IRealtimeReplay
    {
        public async Task<IReadOnlyList<RealtimeMessage>> ReadSinceAsync(
            Guid replayUserId,
            string? lastEventId,
            CancellationToken ct = default)
        {
            replayUserId.ShouldBe(userId);
            lastEventId.ShouldBe("1");

            await registry.DeliverLocal(userId, duplicate);
            await registry.DeliverLocal(userId, next);

            return [duplicate];
        }
    }

    private static async Task<Dictionary<string, string>> ReadSseFrameAsync(
        StreamReader reader,
        CancellationToken ct)
    {
        var frame = new Dictionary<string, string>(StringComparer.Ordinal);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                throw new InvalidOperationException("SSE stream ended before a frame was received.");
            }

            if (line.Length == 0)
            {
                if (frame.Count > 0)
                {
                    return frame;
                }

                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon];
            var value = line[(colon + 1)..].TrimStart();
            frame[key] = frame.TryGetValue(key, out var existing)
                ? existing + "\n" + value
                : value;
        }

        throw new OperationCanceledException(ct);
    }

    private static async Task<Dictionary<string, string>> ReadSseFrameMatchingAsync(
        StreamReader reader,
        Func<string, bool> matchesData,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadSseFrameAsync(reader, ct);
            if (frame.TryGetValue("data", out var data) && matchesData(data))
            {
                return frame;
            }
        }

        throw new OperationCanceledException(ct);
    }
}
