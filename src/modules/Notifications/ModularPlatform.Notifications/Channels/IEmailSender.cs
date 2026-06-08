namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// Sends an email for the email channel. Called from the Worker (durable delivery), never inline in
/// the HTTP request. Implemented over MailKit SMTP.
/// </summary>
internal interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct);
}
