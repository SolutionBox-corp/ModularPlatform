using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Gdpr.Entities;

/// <summary>
/// Per-subject data-encryption-key envelope for crypto-shredding. One row per subject holds the
/// subject's wrapped DEK. Erasure = delete the DEK (set <see cref="DeletedAt"/> and drop the bytes),
/// which renders every ciphertext encrypted under it permanently unrecoverable. The wrapping key
/// (KEK) and its KMS storage are out of scope here — this row models only the envelope lifecycle.
/// Tenant-scoped; audit + xmin concurrency applied by convention.
/// </summary>
internal sealed class SubjectKey : Entity, IUserOwned
{
    public Guid UserId { get; set; }

    /// <summary>The DEK ENCRYPTED under the application master key (penc envelope from <c>ISecretProtector</c>), bound
    /// by AAD to this subject. A DB dump alone cannot read it without the master key. Null once shredded.</summary>
    public byte[]? WrappedDek { get; set; }

    /// <summary>The master-key version that wrapped <see cref="WrappedDek"/> (for key rotation / unwrap).</summary>
    public int DekKeyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class SubjectKeyConfiguration : IEntityTypeConfiguration<SubjectKey>
{
    public void Configure(EntityTypeBuilder<SubjectKey> builder)
    {
        builder.ToTable("subject_keys");
        builder.HasKey(k => k.Id);
        builder.HasIndex(k => k.UserId).IsUnique();
    }
}
