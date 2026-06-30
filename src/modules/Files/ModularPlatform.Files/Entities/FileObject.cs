using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Files.Entities;

/// <summary>
/// Metadata for one uploaded file. The bytes live in <see cref="IFileStorage"/> under <see cref="StorageKey"/>
/// (a server-generated opaque id); this row is the catalog entry. <see cref="IUserOwned"/> → RLS-isolated: a user
/// only ever sees their own files, so the download/list endpoints need no extra ownership check.
/// <see cref="FileName"/> is the ORIGINAL client filename, stored for display ONLY — it is never used to address
/// storage (that is always <see cref="StorageKey"/>).
/// </summary>
internal sealed class FileObject : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}

internal sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> builder)
    {
        builder.ToTable("file_objects");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.UserId).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(256).IsRequired();
        builder.Property(f => f.FileName).HasMaxLength(512).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(f => f.Size).IsRequired();
        builder.HasIndex(f => new { f.UserId, f.CreatedAt, f.Id })
            .IsDescending(false, true, true);
    }
}
