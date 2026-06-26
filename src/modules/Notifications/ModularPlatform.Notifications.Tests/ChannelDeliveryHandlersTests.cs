using ModularPlatform.Notifications.Channels;
using ModularPlatform.Notifications.Contracts;
using ModularPlatform.Notifications.Messaging;
using Shouldly;

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

    private static EmailDeliveryRequested NewEmail(string toAddress, string subject, string body) =>
        new(
            EventId: Guid.CreateVersion7(),
            OccurredAt: DateTimeOffset.UtcNow,
            NotificationId: Guid.CreateVersion7(),
            UserId: Guid.CreateVersion7(),
            ToAddress: toAddress,
            Subject: subject,
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
}
