using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Hosts.Tests;

[Collection(OutOfProcessWorkerCollection.Name)]
public sealed class WorkerEmailDeliveryTests
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Worker_delivers_email_channel_to_smtp_with_the_requested_locale_template()
    {
        await using var fixture = PlatformApiFactory.PublisherOnly();
        await fixture.InitializeAsync();
        await using var smtp = await TestSmtpServer.StartAsync();

        Process? worker = null;
        try
        {
            worker = WorkerProcess.Start(
                fixture.ConnectionString,
                "--Email:Host=127.0.0.1",
                $"--Email:Port={smtp.Port}",
                "--Email:From=no-reply@test.local");

            var (adminId, adminToken) = await RegisterAndLoginAsync(fixture, PlatformApiFactory.AdminEmail);
            var templateKey = $"worker-email-locale-{Guid.CreateVersion7():N}";
            await fixture.ExecuteSqlAsync(
                $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
                $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'en', 'EN Hello {{displayName}}', 'EN Body {{displayName}}')");
            await fixture.ExecuteSqlAsync(
                $"INSERT INTO notification_templates (\"Id\", \"Key\", \"Locale\", \"Subject\", \"Body\") " +
                $"VALUES ('{Guid.CreateVersion7()}', '{templateKey}', 'cs', 'CS Ahoj {{displayName}}', 'CS Telo {{displayName}}')");

            var send = await fixture.Client.SendAsync(fixture.Authed(
                HttpMethod.Post, "/v1/notifications/send", adminToken, new
                {
                    userId = adminId,
                    templateKey,
                    channels = new[] { "email" },
                    data = new Dictionary<string, string>
                    {
                        ["displayName"] = "Ada",
                        ["locale"] = "cs",
                        ["email"] = "ada@example.com",
                    },
                }));
            send.EnsureSuccessStatusCode();

            var message = await smtp.WaitForMessageContainingAsync("CS Ahoj Ada");

            message.ShouldContain("To: ada@example.com");
            message.ShouldContain("Subject: CS Ahoj Ada");
            message.ShouldContain("CS Telo Ada");
            message.ShouldNotContain("EN Hello Ada");
            message.ShouldNotContain("EN Body Ada");
        }
        finally
        {
            if (worker is not null)
            {
                await WorkerProcess.KillAsync(worker);
            }
        }
    }

    private static async Task<(Guid UserId, string AccessToken)> RegisterAndLoginAsync(
        PlatformApiFactory fixture,
        string email)
    {
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
        register.EnsureSuccessStatusCode();
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var accessToken = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
        return (userId, accessToken);
    }

    private sealed class TestSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly object _lock = new();
        private readonly List<string> _messages = [];
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverTask;
        private TaskCompletionSource<string>? _waiter;
        private string? _expectedText;

        private TestSmtpServer(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync();
        }

        public int Port { get; }

        public static Task<TestSmtpServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            return Task.FromResult(new TestSmtpServer(listener));
        }

        public async Task<string> WaitForMessageContainingAsync(string expectedText)
        {
            Task<string> waitTask;
            lock (_lock)
            {
                var existing = _messages.FirstOrDefault(message =>
                    message.Contains(expectedText, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    return existing;
                }

                _expectedText = expectedText;
                _waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _waiter.Task;
            }

            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != waitTask)
            {
                throw new TimeoutException($"SMTP test server did not receive a message containing '{expectedText}' in time.");
            }

            return await waitTask;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();

            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }

            _cts.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            await writer.WriteLineAsync("220 localhost ESMTP");
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null)
                {
                    return;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 8BITMIME");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new List<string>();
                    while (true)
                    {
                        var dataLine = await reader.ReadLineAsync(_cts.Token);
                        if (dataLine is null || dataLine == ".")
                        {
                            break;
                        }

                        data.Add(dataLine);
                    }

                    RecordMessage(string.Join('\n', data));
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }

        private void RecordMessage(string message)
        {
            lock (_lock)
            {
                _messages.Add(message);
                if (_expectedText is not null
                    && message.Contains(_expectedText, StringComparison.OrdinalIgnoreCase))
                {
                    _waiter?.TrySetResult(message);
                }
            }
        }
    }
}
