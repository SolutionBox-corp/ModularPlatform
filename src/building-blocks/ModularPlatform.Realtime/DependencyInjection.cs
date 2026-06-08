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
    /// </summary>
    public static IServiceCollection AddPlatformRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<RealtimeConnectionRegistry>();

        var redisConn = configuration.GetValue<string>("Redis:ConnectionString");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
            services.AddSingleton<IRealtimePublisher, RedisRealtimePublisher>();
            services.AddHostedService<RealtimeRedisSubscriber>();
        }
        else
        {
            services.AddSingleton<IRealtimePublisher, LocalRealtimePublisher>();
        }

        return services;
    }
}

/// <summary>Subscribes to the Redis realtime channels and forwards messages to this instance's live connections.</summary>
internal sealed class RealtimeRedisSubscriber(IConnectionMultiplexer redis, RealtimeConnectionRegistry registry)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Pattern($"{RedisRealtimePublisher.UserChannelPrefix}*"),
            (channel, value) =>
            {
                var userIdText = channel.ToString()[RedisRealtimePublisher.UserChannelPrefix.Length..];
                if (!Guid.TryParse(userIdText, out var userId) || value.IsNullOrEmpty)
                {
                    return;
                }

                var envelope = JsonSerializer.Deserialize<RedisRealtimePublisher.Envelope>((string)value!);
                if (envelope is not null)
                {
                    _ = registry.DeliverLocal(userId, new RealtimeMessage(envelope.EventType, envelope.Json, "0"));
                }
            });
    }
}
