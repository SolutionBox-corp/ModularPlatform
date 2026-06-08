using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// MailKit SMTP implementation of <see cref="IEmailSender"/>. Connects per send (the SMTP server is not
/// required to be live in unit tests — this is exercised only by the Worker against a real relay).
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTlsWhenAvailable, ct);

        if (!string.IsNullOrEmpty(_options.User))
        {
            await client.AuthenticateAsync(_options.User, _options.Password, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
