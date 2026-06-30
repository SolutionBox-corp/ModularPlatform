using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Files.Entities;

/// <summary>
/// User-owned relation between an uploaded file and a product-module object. Files verifies the file owner, but it
/// deliberately does not validate the foreign owner entity; that remains the product module's responsibility.
/// </summary>
internal sealed class FileLink : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid FileObjectId { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
}

internal sealed class FileLinkConfiguration : IEntityTypeConfiguration<FileLink>
{
    public void Configure(EntityTypeBuilder<FileLink> builder)
    {
        builder.ToTable("file_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.UserId).IsRequired();
        builder.Property(l => l.FileObjectId).IsRequired();
        builder.Property(l => l.OwnerType).HasMaxLength(128).IsRequired();
        builder.Property(l => l.OwnerId).IsRequired();
        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => new { l.UserId, l.OwnerType, l.OwnerId, l.CreatedAt, l.Id })
            .IsDescending(false, false, false, true, true);
        builder.HasIndex(l => new { l.UserId, l.OwnerType, l.OwnerId, l.FileObjectId }).IsUnique();
    }
}
