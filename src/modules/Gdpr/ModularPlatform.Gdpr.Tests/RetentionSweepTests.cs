using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Features.Retention.RetentionSweep;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// Integration test for the GDPR retention sweep (<see cref="RetentionSweepCommand"/>).
/// Verifies that shredded <c>subject_keys</c> tombstones (rows with <c>DeletedAt</c> older than the retention
/// window) are hard-deleted, while rows within the retention window or not yet shredded are left untouched.
/// Uses the shared <see cref="PlatformApiFactory"/> harness against a real Testcontainers Postgres.
/// </summary>
[Collection("Integration")]
public sealed class RetentionSweepTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Shredded_subject_key_beyond_retention_window_is_deleted()
    {
        var userId = Guid.CreateVersion7();

        // Seed a shredded subject_key tombstone with DeletedAt 35 days ago (beyond the default 30-day window).
        var oldDeletedAt = DateTimeOffset.UtcNow.AddDays(-35);
        var keyId = Guid.CreateVersion7();
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO subject_keys ("Id", "UserId", "WrappedDek", "CreatedAt", "DeletedAt")
             VALUES ('{keyId}', '{userId}', NULL, '{DateTimeOffset.UtcNow.AddDays(-40):O}', '{oldDeletedAt:O}')
             """);

        // Confirm the row is present before the sweep.
        var before = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"Id\" = '{keyId}'");
        before.ShouldBe(1);

        // Dispatch the retention sweep command through the real DI/dispatcher.
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.Send(new RetentionSweepCommand());

        // The old tombstone should have been deleted.
        result.PurgedCount.ShouldBeGreaterThanOrEqualTo(1);

        var after = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"Id\" = '{keyId}'");
        after.ShouldBe(0);
    }

    [Fact]
    public async Task Shredded_key_within_retention_window_is_not_deleted()
    {
        var userId = Guid.CreateVersion7();

        // Seed a shredded subject_key tombstone with DeletedAt 5 days ago (within the 30-day default window).
        var recentDeletedAt = DateTimeOffset.UtcNow.AddDays(-5);
        var keyId = Guid.CreateVersion7();
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO subject_keys ("Id", "UserId", "WrappedDek", "CreatedAt", "DeletedAt")
             VALUES ('{keyId}', '{userId}', NULL, '{DateTimeOffset.UtcNow.AddDays(-10):O}', '{recentDeletedAt:O}')
             """);

        // Dispatch the retention sweep.
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new RetentionSweepCommand());

        // Row within retention window must NOT be deleted.
        var after = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM subject_keys WHERE \"Id\" = '{keyId}'");
        after.ShouldBe(1);
    }

    [Fact]
    public async Task Live_subject_key_is_not_deleted_by_retention_sweep()
    {
        // A live key (WrappedDek IS NOT NULL, DeletedAt IS NULL) must never be touched.
        var email = $"retention-live-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, "Sup3rSecret!");

        // Wait for the subject key to be created by the audit-PII protector.
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "WrappedDek" IS NOT NULL""",
            1);

        // Run the sweep.
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new RetentionSweepCommand());

        // The live key must still exist.
        var liveCount = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "WrappedDek" IS NOT NULL""");
        liveCount.ShouldBe(1);
    }
}
