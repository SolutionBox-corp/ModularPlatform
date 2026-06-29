using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
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

    [Fact]
    public async Task Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"shred-remint-{Guid.CreateVersion7():N}@x.com", Password);
        var protector = fixture.Services.GetRequiredService<IPersonalDataProtector>();

        var beforeShred = protector.Protect(userId, "still-secret");
        protector.TryReveal(beforeShred, out var plaintext).ShouldBeTrue();
        plaintext.ShouldBe("still-secret");

        await DispatchShredAsync(userId);

        var afterShred = protector.Protect(userId, "must-not-be-written");

        afterShred.ShouldBe(PersonalDataProtection.RedactedMarker);
        protector.TryReveal(beforeShred, out _).ShouldBeFalse();
        (await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' """
            + """AND "WrappedDek" IS NULL AND "DeletedAt" IS NOT NULL""")).ShouldBe(1);
    }

    [Fact]
    public async Task V2_envelope_cannot_be_re_attached_to_another_subject()
    {
        var (subjectA, _) = await fixture.RegisterAndLoginAsync($"shred-aad-a-{Guid.CreateVersion7():N}@x.com", Password);
        var (subjectB, _) = await fixture.RegisterAndLoginAsync($"shred-aad-b-{Guid.CreateVersion7():N}@x.com", Password);
        var protector = fixture.Services.GetRequiredService<IPersonalDataProtector>();

        var envelope = protector.Protect(subjectA, "subject-a-secret");
        protector.Protect(subjectB, "subject-b-key-mint");

        var separator = envelope.LastIndexOf(':');
        separator.ShouldBeGreaterThan(0);
        var tampered = $"penc:v2:{Convert.ToBase64String(subjectB.ToByteArray())}:{envelope[(separator + 1)..]}";

        protector.TryReveal(envelope, out var plaintext).ShouldBeTrue();
        plaintext.ShouldBe("subject-a-secret");
        protector.TryReveal(tampered, out _).ShouldBeFalse();
    }

    private async Task DispatchShredAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new ShredSubjectKeyCommand(userId));
    }
}
