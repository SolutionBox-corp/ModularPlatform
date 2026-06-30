using System.Diagnostics;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Npgsql;
using Shouldly;

namespace ModularPlatform.Hosts.Tests;

[Collection(OutOfProcessWorkerCollection.Name)]
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
            firstWorker = WorkerProcess.Start(fixture.ConnectionString);
            var email = $"ev4-{Guid.CreateVersion7():N}@example.com";
            var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
            register.EnsureSuccessStatusCode();
            var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

            await WaitForWorkerBlockedOnCreditAccountsAsync(fixture.ConnectionString);

            await WorkerProcess.KillAsync(firstWorker);
            firstWorker = null;
            await lockTransaction.RollbackAsync();

            (await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'"))
                .ShouldBe(0);

            restartedWorker = WorkerProcess.Start(fixture.ConnectionString);
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
                await WorkerProcess.KillAsync(firstWorker);
            }

            if (restartedWorker is not null)
            {
                await WorkerProcess.KillAsync(restartedWorker);
            }
        }
    }

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
