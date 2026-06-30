using System.Diagnostics;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Worker;
using Npgsql;
using Shouldly;

namespace ModularPlatform.Hosts.Tests;

public sealed class WorkerDurabilityTests
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task EV4_worker_killed_mid_message_restarts_and_processes_the_durable_event_once()
    {
        await using var fixture = PlatformApiFactory.PublisherOnly();
        await fixture.InitializeAsync();

        await using var lockConnection = new NpgsqlConnection(fixture.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE credit_accounts IN ACCESS EXCLUSIVE MODE",
            lockConnection,
            lockTransaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        Process? firstWorker = null;
        Process? restartedWorker = null;
        try
        {
            firstWorker = StartWorker(fixture.ConnectionString);
            var email = $"ev4-{Guid.CreateVersion7():N}@example.com";
            var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
            register.EnsureSuccessStatusCode();
            var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

            await WaitForWorkerBlockedOnCreditAccountsAsync(fixture.ConnectionString);

            await KillAsync(firstWorker);
            firstWorker = null;
            await lockTransaction.RollbackAsync();

            (await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'"))
                .ShouldBe(0);

            restartedWorker = StartWorker(fixture.ConnectionString);
            await fixture.WaitForCountAsync(
                $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'",
                1,
                attempts: 120);

            (await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'"))
                .ShouldBe(1);
        }
        finally
        {
            if (firstWorker is not null)
            {
                await KillAsync(firstWorker);
            }

            if (restartedWorker is not null)
            {
                await KillAsync(restartedWorker);
            }
        }
    }

    private static Process StartWorker(string connectionString)
    {
        var assemblyPath = typeof(WorkerHostBuilder).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var arg in WorkerArgs(connectionString))
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ModularPlatform.Worker.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task KillAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
        process.Dispose();
    }

    private static string[] WorkerArgs(string connectionString) =>
    [
        "--environment=Testing",
        "--ConnectionStrings:Write", connectionString,
        "--ConnectionStrings:Read", connectionString,
        "--RunMigrationsAtStartup=false",
        "--Messaging:SoloMode=true",
        "--Modules:Identity:Enabled=true",
        "--Modules:Billing:Enabled=true",
        "--Modules:Notifications:Enabled=true",
        "--Modules:Gdpr:Enabled=true",
        "--Modules:Operations:Enabled=true",
        "--Modules:Files:Enabled=true",
        "--Modules:Marketing:Enabled=true",
        "--Modules:Tenancy:Enabled=true",
        "--Persistence:Rls:RuntimePassword=test_app_rls_pwd",
        "--Identity:Auth:AdminEmails:0=admin@platform.test",
        "--Jwt:Issuer=test",
        "--Jwt:Audience=test",
        "--Jwt:SigningKey=integration-test-signing-key-at-least-32b",
        "--Gdpr:Encryption:BlindIndexKey=integration-test-blind-index-key-32ch",
        "--Secrets:MasterKeys:1=aW50ZWdyYXRpb24tdGVzdC1zZWNyZXRzLWtleS0wMzI=",
        "--RateLimiting:GlobalPermitsPerMinute=100000",
        "--RateLimiting:AuthPermitsPerMinute=100000",
        "--Storage:Provider=local",
        "--Storage:Local:RootPath=" + Path.Combine(Path.GetTempPath(), $"mp-worker-storage-{Guid.CreateVersion7():N}"),
        "--Marketing:UseFakeGateways=true",
        "--Billing:Stripe:UseFakeGateway=true",
        "--Billing:Stripe:SuccessUrl=https://app.test/billing/success",
        "--Billing:Stripe:CancelUrl=https://app.test/billing/cancel",
        "--Billing:Subscriptions:Plans:0:PlanKey=pro",
        "--Billing:Subscriptions:Plans:0:StripePriceId=price_test_pro",
        "--Billing:Subscriptions:Plans:0:CreditsPerPeriod=100",
        "--Platform:Payments:Provider=fake",
        "--Platform:Payments:Currency=EUR",
        "--Platform:Payments:Plans:pro:AmountMinorUnits=4900",
        "--Platform:Payments:Plans:pro:Currency=EUR",
        "--Platform:Payments:Plans:pro:Description=Pro plan",
    ];

    private static async Task WaitForWorkerBlockedOnCreditAccountsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT count(*)::bigint
                FROM pg_stat_activity
                WHERE wait_event_type = 'Lock'
                  AND query ILIKE '%credit_accounts%'
                """,
                conn);

            if ((long)(await cmd.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Worker did not block on credit_accounts in time.");
    }
}
