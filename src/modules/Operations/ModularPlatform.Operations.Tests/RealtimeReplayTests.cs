using Microsoft.Extensions.Options;
using ModularPlatform.Realtime;
using Shouldly;

namespace ModularPlatform.Operations.Tests;

/// <summary>
/// Unit tests for the <see cref="LocalRealtimePublisher"/> in-memory ring buffer and
/// <see cref="IRealtimeReplay"/> semantics. The Redis variant requires a live Redis instance and
/// is not unit-tested here. The SSE streaming path is not tested over TestServer
/// (buffered responses — see <see cref="RealtimeSseTests"/> notes).
/// </summary>
public sealed class RealtimeReplayTests
{
    private static LocalRealtimePublisher CreatePublisher(int maxEvents = 10, bool enabled = true) =>
        new(new RealtimeConnectionRegistry(),
            Options.Create(new RealtimeReplayOptions { Enabled = enabled, MaxEvents = maxEvents, TtlMinutes = 60 }));

    [Fact]
    public async Task ReadSinceAsync_with_null_or_empty_lastEventId_returns_empty()
    {
        var pub = CreatePublisher();
        var userId = Guid.CreateVersion7();
        await pub.PublishToUserAsync(userId, "evt", new { });

        (await pub.ReadSinceAsync(userId, null)).Count.ShouldBe(0);
        (await pub.ReadSinceAsync(userId, "")).Count.ShouldBe(0);
        (await pub.ReadSinceAsync(userId, "  ")).Count.ShouldBe(0);
    }

    [Fact]
    public async Task ReadSinceAsync_returns_only_events_newer_than_cursor()
    {
        var pub = CreatePublisher();
        var userId = Guid.CreateVersion7();

        // Publish 5 events; capture the id of the third one.
        string? cursorId = null;
        for (var i = 1; i <= 5; i++)
        {
            await pub.PublishToUserAsync(userId, $"e{i}", new { });
            if (i == 3)
            {
                // The buffer contains e1..e3 at this point; the last id belongs to e3.
                var snapshot = await pub.ReadSinceAsync(userId, "0");
                cursorId = snapshot.Last().Id; // id of e3
            }
        }

        var after = await pub.ReadSinceAsync(userId, cursorId!);

        after.Count.ShouldBe(2);
        after[0].EventType.ShouldBe("e4");
        after[1].EventType.ShouldBe("e5");
    }

    [Fact]
    public async Task ReadSinceAsync_at_last_event_returns_empty()
    {
        var pub = CreatePublisher();
        var userId = Guid.CreateVersion7();

        await pub.PublishToUserAsync(userId, "x", new { });
        var all = await pub.ReadSinceAsync(userId, "0");
        var lastId = all.Last().Id;

        (await pub.ReadSinceAsync(userId, lastId)).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Local_replay_treats_a_future_cursor_as_stale_process_lifetime_and_replays_the_current_buffer()
    {
        var pub = CreatePublisher();
        var userId = Guid.CreateVersion7();

        await pub.PublishToUserAsync(userId, "after-restart-1", new { });
        await pub.PublishToUserAsync(userId, "after-restart-2", new { });

        var replay = await pub.ReadSinceAsync(userId, "5000");

        replay.Select(message => message.EventType).ShouldBe(["after-restart-1", "after-restart-2"]);
    }

    [Fact]
    public async Task Ring_buffer_bounded_to_maxEvents_evicts_oldest_first()
    {
        const int max = 3;
        var pub = CreatePublisher(maxEvents: max);
        var userId = Guid.CreateVersion7();

        // Publish max+2 events — only the last `max` are retained.
        for (var i = 1; i <= max + 2; i++)
        {
            await pub.PublishToUserAsync(userId, $"e{i}", new { });
        }

        // Reading from "0" (a cursor lower than any real id) returns at most `max` events.
        var all = await pub.ReadSinceAsync(userId, "0");
        all.Count.ShouldBe(max);
        // The retained events should be the newest ones (e3, e4, e5).
        all[0].EventType.ShouldBe("e3");
        all[1].EventType.ShouldBe("e4");
        all[2].EventType.ShouldBe("e5");
    }

    [Fact]
    public async Task ReadSinceAsync_for_unknown_userId_returns_empty()
    {
        var pub = CreatePublisher();
        var result = await pub.ReadSinceAsync(Guid.CreateVersion7(), "0");
        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Events_from_different_users_are_isolated()
    {
        var pub = CreatePublisher();
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        await pub.PublishToUserAsync(alice, "alice_event", new { });
        await pub.PublishToUserAsync(bob, "bob_event", new { });

        var aliceAll = await pub.ReadSinceAsync(alice, "0");
        aliceAll.ShouldAllBe(m => m.EventType == "alice_event");
        aliceAll.Count.ShouldBe(1);

        var bobAll = await pub.ReadSinceAsync(bob, "0");
        bobAll.ShouldAllBe(m => m.EventType == "bob_event");
        bobAll.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_replay_buffer_returns_empty_from_ReadSince()
    {
        var pub = CreatePublisher(enabled: false);
        var userId = Guid.CreateVersion7();
        await pub.PublishToUserAsync(userId, "event", new { });

        (await pub.ReadSinceAsync(userId, "0")).Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("42", "42-1")]
    [InlineData("1700000000000-3", "1700000000000-4")]
    [InlineData("1700000000000-not-a-number", "1700000000000-not-a-number")]
    [InlineData("not-a-stream-id", "not-a-stream-id")]
    [InlineData("1700000000000-18446744073709551615", "1700000000001-0")]
    public void Redis_stream_cursor_increment_handles_missing_malformed_and_overflow_sequence(
        string input,
        string expected)
    {
        RedisRealtimePublisher.IncrementStreamId(input).ShouldBe(expected);
    }
}
