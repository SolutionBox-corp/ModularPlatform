using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Features.Erasure.ShredSubjectKey;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// Crypto-shred semantics, exercised against the REAL <see cref="ShredSubjectKeyHandler"/> (dispatched in-process,
/// like the erasure flow does) — NOT a private re-implementation that could silently drift from the handler.
/// Registration mints the subject's DEK (it encrypts the email); shredding drops <c>WrappedDek</c> + stamps
/// <c>DeletedAt</c>, and a replay must not re-stamp the first erasure timestamp.
/// </summary>
[Collection("Integration")]
public sealed class SubjectKeyShredTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Shred_drops_the_dek_and_stamps_deleted_at()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"shred-{Guid.CreateVersion7():N}@x.com", Password);
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "WrappedDek" IS NOT NULL""", 1);

        await DispatchShredAsync(userId);

        (await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' """
            + """AND "WrappedDek" IS NULL AND "DeletedAt" IS NOT NULL""")).ShouldBe(1);
    }

    [Fact]
    public async Task Shred_is_idempotent_and_preserves_the_first_erasure_timestamp()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"shred2-{Guid.CreateVersion7():N}@x.com", Password);
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}'""", 1);

        await DispatchShredAsync(userId);
        var firstDeletedAt = await fixture.ScalarAsync<string>(
            $"""SELECT "DeletedAt"::text FROM subject_keys WHERE "UserId" = '{userId}'""");

        await DispatchShredAsync(userId); // replay — the guard must leave the already-shredded key untouched
        var secondDeletedAt = await fixture.ScalarAsync<string>(
            $"""SELECT "DeletedAt"::text FROM subject_keys WHERE "UserId" = '{userId}'""");

        secondDeletedAt.ShouldBe(firstDeletedAt, "a replayed shred must not re-stamp the erasure timestamp");
    }

    private async Task DispatchShredAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new ShredSubjectKeyCommand(userId));
    }
}
