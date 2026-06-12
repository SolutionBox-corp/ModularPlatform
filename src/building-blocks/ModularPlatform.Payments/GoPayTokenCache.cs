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

        // 60s safety margin so an in-flight request never uses a token that expires mid-call.
        if (entry.Token is { } cached && clock.UtcNow < entry.ExpiresAt.AddSeconds(-60))
        {
            return cached;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.Token is { } stillCached && clock.UtcNow < entry.ExpiresAt.AddSeconds(-60))
            {
                return stillCached;
            }

            var (token, expiresAt) = await fetch(ct);
            entry.Token = token;
            entry.ExpiresAt = expiresAt;
            return token;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private sealed class Entry
    {
        public string? Token;
        public DateTimeOffset ExpiresAt;
        public readonly SemaphoreSlim Lock = new(1, 1);
    }
}
