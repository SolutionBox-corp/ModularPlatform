using Microsoft.Extensions.Options;
using ModularPlatform.Realtime;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class RedisRealtimeReplayTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();
    private IConnectionMultiplexer? _connection;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task Redis_replay_reads_only_events_after_the_last_event_id_and_refreshes_ttl()
    {
        var publisher = CreatePublisher(maxEvents: 10, ttlMinutes: 1);
        var userId = Guid.CreateVersion7();

        await publisher.PublishToUserAsync(userId, "one", new { value = 1 });
        await publisher.PublishToUserAsync(userId, "two", new { value = 2 });
        await publisher.PublishToUserAsync(userId, "three", new { value = 3 });

        var all = await publisher.ReadSinceAsync(userId, "0-0");
        all.Select(message => message.EventType).ShouldBe(["one", "two", "three"]);
        all.ShouldAllBe(message => message.Id.Contains('-', StringComparison.Ordinal));

        var afterFirst = await publisher.ReadSinceAsync(userId, all[0].Id);
        afterFirst.Select(message => message.EventType).ShouldBe(["two", "three"]);

        var afterLast = await publisher.ReadSinceAsync(userId, all[^1].Id);
        afterLast.ShouldBeEmpty();

        var ttl = await _connection!.GetDatabase()
            .KeyTimeToLiveAsync($"{RedisRealtimePublisher.StreamKeyPrefix}{userId}");
        ttl.ShouldNotBeNull();
        ttl!.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Redis_replay_is_user_scoped_and_disabled_mode_does_not_store_a_stream()
    {
        var publisher = CreatePublisher(maxEvents: 10, ttlMinutes: 1);
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        await publisher.PublishToUserAsync(alice, "alice-event", new { owner = "alice" });
        await publisher.PublishToUserAsync(bob, "bob-event", new { owner = "bob" });

        var aliceReplay = await publisher.ReadSinceAsync(alice, "0-0");
        aliceReplay.Single().EventType.ShouldBe("alice-event");

        var disabled = new RedisRealtimePublisher(
            _connection!,
            Options.Create(new RealtimeReplayOptions { Enabled = false, MaxEvents = 10, TtlMinutes = 1 }));
        var disabledUser = Guid.CreateVersion7();

        await disabled.PublishToUserAsync(disabledUser, "disabled-event", new { ok = true });

        (await disabled.ReadSinceAsync(disabledUser, "0-0")).ShouldBeEmpty();
        (await _connection!.GetDatabase()
            .KeyExistsAsync($"{RedisRealtimePublisher.StreamKeyPrefix}{disabledUser}"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Redis_subscriber_forwards_user_channel_messages_to_the_local_registry_with_the_stream_id()
    {
        var registry = new RealtimeConnectionRegistry();
        var subscriber = new RealtimeRedisSubscriber(_connection!, registry);
        var publisher = CreatePublisher(maxEvents: 10, ttlMinutes: 1);
        var userId = Guid.CreateVersion7();
        var received = new TaskCompletionSource<RealtimeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = registry.Subscribe(userId, message =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        });

        await subscriber.StartAsync(CancellationToken.None);
        try
        {
            for (var attempt = 0; attempt < 10 && !received.Task.IsCompleted; attempt++)
            {
                await publisher.PublishToUserAsync(userId, "fanout", new { ok = true });
                await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromMilliseconds(100)));
            }

            var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.ShouldBe(received.Task);

            var message = await received.Task;
            message.EventType.ShouldBe("fanout");
            message.Json.ShouldContain("\"ok\":true");
            message.Id.ShouldNotBe("0");
            message.Id.ShouldContain("-");
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
            subscriber.Dispose();
        }
    }

    private RedisRealtimePublisher CreatePublisher(int maxEvents, int ttlMinutes) =>
        new(_connection!, Options.Create(new RealtimeReplayOptions
        {
            Enabled = true,
            MaxEvents = maxEvents,
            TtlMinutes = ttlMinutes,
        }));
}
