using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Jobs;

internal sealed class MessagingHealthAlertOptions
{
    public const string SectionName = "Messaging:HealthAlerts";

    public string? WebhookUrl { get; init; }

    public int TimeoutSeconds { get; init; } = 5;
}

internal interface IMessagingHealthAlertSink
{
    Task NotifyAsync(MessagingHealthEvaluation.Result evaluation, CancellationToken ct);
}

internal sealed class NoOpMessagingHealthAlertSink : IMessagingHealthAlertSink
{
    public Task NotifyAsync(MessagingHealthEvaluation.Result evaluation, CancellationToken ct) =>
        Task.CompletedTask;
}

internal sealed class WebhookMessagingHealthAlertSink(
    IHttpClientFactory httpClientFactory,
    IOptions<MessagingHealthAlertOptions> options,
    ILogger<WebhookMessagingHealthAlertSink> logger,
    IClock clock) : IMessagingHealthAlertSink
{
    public async Task NotifyAsync(MessagingHealthEvaluation.Result evaluation, CancellationToken ct)
    {
        if (evaluation.Warnings.Count == 0 || string.IsNullOrWhiteSpace(options.Value.WebhookUrl))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        try
        {
            var client = httpClientFactory.CreateClient("messaging-health-alerts");
            using var response = await client.PostAsJsonAsync(
                options.Value.WebhookUrl,
                new MessagingHealthAlertPayload(
                    OccurredAtUtc: clock.UtcNow,
                    DeadLetters: evaluation.DeadLetters,
                    IncomingPending: evaluation.IncomingPending,
                    OutgoingPending: evaluation.OutgoingPending,
                    Warnings: evaluation.Warnings),
                timeout.Token);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogError(ex, "Messaging health alert webhook delivery failed.");
        }
    }
}

internal sealed record MessagingHealthAlertPayload(
    DateTimeOffset OccurredAtUtc,
    int DeadLetters,
    int IncomingPending,
    int OutgoingPending,
    IReadOnlyList<string> Warnings);
