using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A refresh token in a rotation family. On refresh the current token is consumed and replaced;
/// replaying a consumed token (reuse) means the family is compromised → the whole family is revoked.
/// Token VALUE is stored hashed (never plaintext). Flat — references UserId, no navigation.
/// </summary>
internal sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => ConsumedAt is null && RevokedAt is null && ExpiresAt > now;
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);
    }
}
