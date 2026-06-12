using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Abstractions;
using StackExchange.Redis;

namespace ModularPlatform.Realtime;

public static class RealtimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the realtime fan-out. With <c>Redis:ConnectionString</c> configured: a Redis-backed
    /// <see cref="IRealtimePublisher"/> + a subscriber that forwards messages to local SSE connections (multi-instance).
    /// Without Redis: a single-instance local publisher. The registry + port are always available, so producers
    /// (handlers, worker) never change when you scale out.
    /// Also registers <see cref="IRealtimeReplay"/> for Last-Event-ID reconnect replay.
    /// </summary>
    /// <param name="withStreamListener">
    /// True ONLY for the host that serves the SSE stream (the Api). The Redis→local-SSE subscriber (and its
    /// PSUBSCRIBE) is pointless in the Worker/Jobs/Migration hosts — they hold no browser connections, so they'd
    /// forward every fanned-out event to an empty local registry. Those hosts still get the PUBLISHER (they produce
    /// events); only the Api LISTENS.
    /// </param>
    public static IServiceCollection AddPlatformRealtime(
        this IServiceCollection services, IConfiguration configuration, bool withStreamListener = false)
    {
        services.Configure<RealtimeReplayOptions>(configuration.GetSection(RealtimeReplayOptions.SectionName));
        services.AddSingleton<RealtimeConnectionRegistry>();

        var redisConn = configuration.GetValue<string>("Redis:ConnectionString");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            // AbortOnConnectFail=false: if Redis is configured but momentarily unreachable (k8s/compose startup
            // ordering), the host starts and the multiplexer reconnects in the background instead of crash-looping.
            var options = ConfigurationOptions.Parse(redisConn);
            options.AbortOnConnectFail = false;
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
            // RedisRealtimePublisher implements BOTH IRealtimePublisher and IRealtimeReplay.
            services.AddSingleton<RedisRealtimePublisher>();
            services.AddSingleton<IRealtimePublisher>(sp => sp.GetRequiredService<RedisRealtimePublisher>());
            services.AddSingleton<IRealtimeReplay>(sp => sp.GetRequiredService<RedisRealtimePublisher>());
            if (withStreamListener)
            {
                services.AddHostedService<RealtimeRedisSubscriber>();
            }
        }
        else
        {
            // LocalRealtimePublisher implements BOTH IRealtimePublisher and IRealtimeReplay.
            services.AddSingleton<LocalRealtimePublisher>();
            services.AddSingleton<IRealtimePublisher>(sp => sp.GetRequiredService<LocalRealtimePublisher>());
            services.AddSingleton<IRealtimeReplay>(sp => sp.GetRequiredService<LocalRealtimePublisher>());
        }

        return services;
    }
}

/// <summary>Subscribes to the Redis realtime channels and forwards messages to this instance's live connections.
/// Reads the stream Id from the envelope so that SSE clients receive the correct Last-Event-ID.</summary>
internal sealed class RealtimeRedisSubscriber(IConnectionMultiplexer redis, RealtimeConnectionRegistry registry)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Pattern($"{RedisRealtimePublisher.UserChannelPrefix}*");
        await subscriber.SubscribeAsync(channel, (ch, value) =>
        {
            var userIdText = ch.ToString()[RedisRealtimePublisher.UserChannelPrefix.Length..];
            if (!Guid.TryParse(userIdText, out var userId) || value.IsNullOrEmpty)
            {
                return;
            }

            var envelope = JsonSerializer.Deserialize<RedisRealtimePublisher.Envelope>((string)value!);
            if (envelope is not null)
            {
                // Use the stream Id from the envelope (not a hard-coded "0") so SSE clients get the
                // correct Last-Event-ID cursor for replay on reconnect.
                _ = registry.DeliverLocal(userId, new RealtimeMessage(envelope.EventType, envelope.Json, envelope.Id));
            }
        });

        // Stay alive until shutdown, then unsubscribe cleanly (the base BackgroundService would otherwise treat the
        // method returning right after SubscribeAsync as "completed" and never release the subscription).
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }

        await subscriber.UnsubscribeAsync(channel);
    }
}
