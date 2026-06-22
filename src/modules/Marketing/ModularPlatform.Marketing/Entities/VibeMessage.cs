using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// One turn in a <see cref="VibeConversation"/>. <see cref="IUserOwned"/> → RLS-isolated. The agent loop's tool calls
/// for an assistant turn are kept verbatim in <see cref="ToolCallsJson"/> so the UI can replay the reasoning trace.
/// </summary>
internal sealed class VibeMessage : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }

    /// <summary><c>user</c> | <c>assistant</c> | <c>system</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Tool calls + results for an assistant turn, as JSON. Null for plain text turns.</summary>
    public string? ToolCallsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class VibeMessageConfiguration : IEntityTypeConfiguration<VibeMessage>
{
    public void Configure(EntityTypeBuilder<VibeMessage> builder)
    {
        builder.ToTable("vibe_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.Role).HasMaxLength(16).IsRequired();
        builder.Property(m => m.ToolCallsJson).HasColumnType("jsonb");
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
