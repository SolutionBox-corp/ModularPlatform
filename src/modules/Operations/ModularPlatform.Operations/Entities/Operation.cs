using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Operations.Entities;

/// <summary>
/// A tracked long-running operation. <see cref="IUserOwned"/> → RLS-isolated: a user only ever sees their own
/// operations, so the status endpoint needs no extra ownership check. Created <see cref="OperationStatus.Pending"/>
/// by the accepting request, advanced to a terminal state by the durable worker.
/// </summary>
internal sealed class Operation : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public OperationStatus Status { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.UserId).IsRequired();
        builder.Property(o => o.Type).HasMaxLength(128).IsRequired();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(256);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(o => o.ErrorCode).HasMaxLength(128);
        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => new { o.UserId, o.Type, o.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
