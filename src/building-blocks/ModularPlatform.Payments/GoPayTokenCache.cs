using System.Collections.Concurrent;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Payments;

/// <summary>
/// Process-wide cache of GoPay OAuth <c>client_credentials</c> bearer tokens, keyed by client id. The resolver builds a
/// fresh <see cref="GoPayPaymentGateway"/> per request (each carries one tenant's credentials), so a per-instance cache
/// would re-auth on every call. Registered as a SINGLETON and shared across those throwaway gateways, one bearer is
/// fetched per (client) per ~30 min instead of per request — a single in-flight fetch per key (double-checked lock).
/// </summary>
public sealed class GoPayTokenCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<string> GetAsync(
        string clientId,
        IClock clock,
        Func<CancellationToken, Task<(string Token, DateTimeOffset ExpiresAt)>> fetch,
        CancellationToken ct)
    {
        var entry = _entries.GetOrAdd(clientId, static _ => new Entry());

        // The cached (token + expiry) is a single immutable reference read via the volatile field, so a lock-free
        // fast-path reader can never see a torn token/expiry pair (matters on weak memory models, e.g. ARM64).
        // 60s safety margin so an in-flight request never uses a token that expires mid-call.
        if (entry.Cached is { } cached && clock.UtcNow < cached.ExpiresAt.AddSeconds(-60))
        {
            return cached.Token;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.Cached is { } stillCached && clock.UtcNow < stillCached.ExpiresAt.AddSeconds(-60))
            {
                return stillCached.Token;
            }

            var (token, expiresAt) = await fetch(ct);
            entry.Cached = new CachedToken(token, expiresAt);
            return token;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);

    private sealed class Entry
    {
        private CachedToken? _cached;

        public CachedToken? Cached
        {
            get => Volatile.Read(ref _cached);
            set => Volatile.Write(ref _cached, value);
        }

        public readonly SemaphoreSlim Lock = new(1, 1);
    }
}
