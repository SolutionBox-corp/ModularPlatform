using ModularPlatform.Web.RateLimiting;
using Shouldly;
using StackExchange.Redis;
using System.Threading.RateLimiting;
using Testcontainers.Redis;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class RedisRateLimiterTests : IAsyncLifetime
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
    public async Task Redis_fixed_window_limiter_shares_the_same_bucket_across_instances()
    {
        var key = $"rl:test:{Guid.CreateVersion7():N}";
        var db = _connection!.GetDatabase();
        var instanceA = new RedisFixedWindowRateLimiter(db, key, permitLimit: 2, TimeSpan.FromMinutes(1));
        var instanceB = new RedisFixedWindowRateLimiter(db, key, permitLimit: 2, TimeSpan.FromMinutes(1));

        (await instanceA.AcquireAsync()).IsAcquired.ShouldBeTrue();
        (await instanceB.AcquireAsync()).IsAcquired.ShouldBeTrue();

        var denied = await instanceA.AcquireAsync();

        denied.IsAcquired.ShouldBeFalse();
        denied.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
        (await db.StringGetAsync(key)).ToString().ShouldBe("3");
    }

    [Fact]
    public async Task Redis_fixed_window_limiter_expires_the_shared_bucket()
    {
        var key = $"rl:test:{Guid.CreateVersion7():N}";
        var db = _connection!.GetDatabase();
        var limiter = new RedisFixedWindowRateLimiter(db, key, permitLimit: 1, TimeSpan.FromMilliseconds(250));

        (await limiter.AcquireAsync()).IsAcquired.ShouldBeTrue();
        (await limiter.AcquireAsync()).IsAcquired.ShouldBeFalse();

        await Task.Delay(TimeSpan.FromMilliseconds(350));

        (await limiter.AcquireAsync()).IsAcquired.ShouldBeTrue();
    }
}
