using ModularPlatform.Notifications.Channels;
using ModularPlatform.Notifications.Contracts;
using ModularPlatform.Notifications.Messaging;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Net;
using System.Text.Json;

namespace ModularPlatform.Notifications.Tests;

public sealed class ChannelDeliveryHandlersTests
{
    [Fact]
    public async Task Email_delivery_handler_sends_exact_rendered_payload_to_email_sender()
    {
        var sender = new RecordingEmailSender();
        var message = NewEmail(toAddress: "ada@example.com", subject: "Hello Ada", body: "Private body");

        await new EmailDeliveryHandler().Handle(message, sender, CancellationToken.None);

        sender.Calls.ShouldBe(1);
        sender.LastToAddress.ShouldBe("ada@example.com");
        sender.LastSubject.ShouldBe("Hello Ada");
        sender.LastBody.ShouldBe("Private body");
    }

    [Fact]
    public async Task Email_delivery_handler_skips_missing_address_without_calling_smtp()
    {
        var sender = new RecordingEmailSender();
        var message = NewEmail(toAddress: "", subject: "Hello", body: "Body");

        await new EmailDeliveryHandler().Handle(message, sender, CancellationToken.None);

        sender.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Email_delivery_handler_propagates_smtp_failures_for_wolverine_retry_and_dlq()
    {
        var sender = new RecordingEmailSender
        {
            ExceptionToThrow = new InvalidOperationException("smtp down"),
        };
        var message = NewEmail(toAddress: "ada@example.com", subject: "Hello", body: "Body");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => new EmailDeliveryHandler().Handle(message, sender, CancellationToken.None));

        ex.Message.ShouldBe("smtp down");
        sender.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Push_delivery_handler_sends_exact_rendered_payload_to_push_sender()
    {
        var sender = new RecordingPushSender();
        var userId = Guid.CreateVersion7();
        var message = NewPush(userId, title: "Deal assigned", body: "Open deal A");

        await new PushDeliveryHandler().Handle(message, sender, CancellationToken.None);

        sender.Calls.ShouldBe(1);
        sender.LastUserId.ShouldBe(userId);
        sender.LastTitle.ShouldBe("Deal assigned");
        sender.LastBody.ShouldBe("Open deal A");
    }

    [Fact]
    public async Task Push_delivery_handler_propagates_provider_failures_for_wolverine_retry_and_dlq()
    {
        var sender = new RecordingPushSender
        {
            ExceptionToThrow = new InvalidOperationException("push provider down"),
        };
        var message = NewPush(Guid.CreateVersion7(), title: "Deal assigned", body: "Open deal A");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => new PushDeliveryHandler().Handle(message, sender, CancellationToken.None));

        ex.Message.ShouldBe("push provider down");
        sender.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Noop_push_sender_completes_without_external_provider()
    {
        var sender = new NoOpPushSender();

        await sender.SendAsync(Guid.CreateVersion7(), "Deal assigned", "Open deal A", CancellationToken.None);
    }

    [Fact]
    public async Task Webhook_push_sender_posts_the_exact_push_payload()
    {
        var userId = Guid.CreateVersion7();
        var handler = new CapturingHttpHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var sender = new WebhookPushSender(
            new SingleClientFactory(new HttpClient(handler)),
            Options.Create(new PushOptions
            {
                WebhookUrl = "https://push.test/deliver",
                TimeoutSeconds = 5,
            }));

        await sender.SendAsync(userId, "Deal assigned", "Open deal A", CancellationToken.None);

        handler.Requests.ShouldHaveSingleItem();
        var request = handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://push.test/deliver");
        var payload = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        payload.GetProperty("userId").GetGuid().ShouldBe(userId);
        payload.GetProperty("title").GetString().ShouldBe("Deal assigned");
        payload.GetProperty("body").GetString().ShouldBe("Open deal A");
    }

    [Fact]
    public async Task Webhook_push_sender_propagates_provider_failures_for_wolverine_retry_and_dlq()
    {
        var sender = new WebhookPushSender(
            new SingleClientFactory(new HttpClient(new CapturingHttpHandler(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))),
            Options.Create(new PushOptions
            {
                WebhookUrl = "https://push.test/deliver",
                TimeoutSeconds = 5,
            }));

        await Should.ThrowAsync<HttpRequestException>(
            () => sender.SendAsync(Guid.CreateVersion7(), "Deal assigned", "Open deal A", CancellationToken.None));
    }

    private static EmailDeliveryRequested NewEmail(string toAddress, string subject, string body) =>
        new(
            EventId: Guid.CreateVersion7(),
            OccurredAt: DateTimeOffset.UtcNow,
            NotificationId: Guid.CreateVersion7(),
            UserId: Guid.CreateVersion7(),
            ToAddress: toAddress,
            Subject: subject,
            Body: body);

    private static PushDeliveryRequested NewPush(Guid userId, string title, string body) =>
        new(
            EventId: Guid.CreateVersion7(),
            OccurredAt: DateTimeOffset.UtcNow,
            NotificationId: Guid.CreateVersion7(),
            UserId: userId,
            Title: title,
            Body: body);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public int Calls { get; private set; }
        public string? LastToAddress { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastBody { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
        {
            Calls++;
            LastToAddress = toAddress;
            LastSubject = subject;
            LastBody = body;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPushSender : IPushSender
    {
        public int Calls { get; private set; }
        public Guid LastUserId { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastBody { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task SendAsync(Guid userId, string title, string body, CancellationToken ct)
        {
            Calls++;
            LastUserId = userId;
            LastTitle = title;
            LastBody = body;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CapturingHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return response;
        }
    }
}
