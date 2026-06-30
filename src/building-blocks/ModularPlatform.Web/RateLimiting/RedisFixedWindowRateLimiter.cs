using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace ModularPlatform.Web.RateLimiting;

internal sealed class RedisFixedWindowRateLimiter(
    IDatabase redis,
    string key,
    int permitLimit,
    TimeSpan window) : RateLimiter
{
    private const string Script = """
        local current = redis.call('INCRBY', KEYS[1], ARGV[2])
        if current == tonumber(ARGV[2]) then
          redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        return { current, ttl }
        """;

    private long _failedLeases;
    private long _successfulLeases;

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics GetStatistics()
    {
        var current = 0L;
        try
        {
            var value = redis.StringGet(key);
            if (value.HasValue && long.TryParse(value.ToString(), out var parsed))
            {
                current = parsed;
            }
        }
        catch (RedisException)
        {
            current = permitLimit;
        }

        return new RateLimiterStatistics
        {
            CurrentAvailablePermits = Math.Max(0, permitLimit - current),
            CurrentQueuedCount = 0,
            TotalFailedLeases = Interlocked.Read(ref _failedLeases),
            TotalSuccessfulLeases = Interlocked.Read(ref _successfulLeases),
        };
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
        => AcquireAsyncCore(permitCount, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken)
    {
        if (permitCount <= 0 || permitCount > permitLimit)
        {
            Interlocked.Increment(ref _failedLeases);
            return RedisRateLimitLease.Failed(window);
        }

        try
        {
            var result = (RedisResult[])(await redis.ScriptEvaluateAsync(
                Script,
                [key],
                [(long)window.TotalMilliseconds, permitCount]).WaitAsync(cancellationToken))!;

            var current = (long)result[0];
            var ttlMs = (long)result[1];
            if (current <= permitLimit)
            {
                Interlocked.Increment(ref _successfulLeases);
                return RedisRateLimitLease.Success;
            }

            Interlocked.Increment(ref _failedLeases);
            var retryAfter = ttlMs > 0
                ? TimeSpan.FromMilliseconds(ttlMs)
                : window;
            return RedisRateLimitLease.Failed(retryAfter);
        }
        catch (RedisException)
        {
            Interlocked.Increment(ref _failedLeases);
            return RedisRateLimitLease.Failed(TimeSpan.FromSeconds(5));
        }
    }
}

internal sealed class RedisRateLimitLease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
{
    public static readonly RedisRateLimitLease Success = new(acquired: true, retryAfter: null);

    public override bool IsAcquired => acquired;

    public override IEnumerable<string> MetadataNames =>
        retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

    public static RedisRateLimitLease Failed(TimeSpan retryAfter) =>
        new(acquired: false, retryAfter);

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (retryAfter is not null && metadataName == MetadataName.RetryAfter.Name)
        {
            metadata = retryAfter.Value;
            return true;
        }

        metadata = null;
        return false;
    }
}
