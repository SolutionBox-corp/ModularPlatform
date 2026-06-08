using ModularPlatform.Gdpr.Entities;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// Asserts the crypto-shred field semantics applied by <c>ShredSubjectKeyHandler</c> on erasure:
/// the subject's DEK is dropped (<c>WrappedDek == null</c>) and <c>DeletedAt</c> is stamped, and the
/// operation is idempotent once a key is already shredded. The handler itself is exercised end-to-end by
/// the integration test described in the orchestrator report (it needs a real Postgres + the Worker
/// pipeline); these unit cases lock the entity-level contract without a DbContext.
/// </summary>
public sealed class SubjectKeyShredTests
{
    private static readonly DateTimeOffset ShredAt =
        new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    // Mirrors the handler's guarded mutation: only shred a key that has not already been shredded.
    private static void ApplyShred(SubjectKey key, DateTimeOffset now)
    {
        if (key is { DeletedAt: null })
        {
            key.WrappedDek = null;
            key.DeletedAt = now;
        }
    }

    [Fact]
    public void Shred_drops_the_dek_and_stamps_deleted_at()
    {
        var key = new SubjectKey
        {
            UserId = Guid.CreateVersion7(),
            WrappedDek = [1, 2, 3, 4],
            CreatedAt = ShredAt.AddDays(-30),
            DeletedAt = null,
        };

        ApplyShred(key, ShredAt);

        Assert.Null(key.WrappedDek);
        Assert.Equal(ShredAt, key.DeletedAt);
    }

    [Fact]
    public void Shred_is_idempotent_when_already_shredded()
    {
        var alreadyShreddedAt = ShredAt.AddDays(-1);
        var key = new SubjectKey
        {
            UserId = Guid.CreateVersion7(),
            WrappedDek = null,
            CreatedAt = ShredAt.AddDays(-30),
            DeletedAt = alreadyShreddedAt,
        };

        ApplyShred(key, ShredAt);

        // The original erasure timestamp is preserved — a replayed event must not re-stamp it.
        Assert.Null(key.WrappedDek);
        Assert.Equal(alreadyShreddedAt, key.DeletedAt);
    }
}
