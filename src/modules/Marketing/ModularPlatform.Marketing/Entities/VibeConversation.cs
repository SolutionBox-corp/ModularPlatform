using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// A "vibe marketing" agentic chat thread (Robootec vibetrading pattern). <see cref="IUserOwned"/> → RLS-isolated.
/// Soft-deletable so the sidebar can hide it while history is retained; messages reference it by <c>ConversationId</c>.
/// </summary>
internal sealed class VibeConversation : AuditableEntity, IUserOwned, ISoftDeletable
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class VibeConversationConfiguration : IEntityTypeConfiguration<VibeConversation>
{
    public void Configure(EntityTypeBuilder<VibeConversation> builder)
    {
        builder.ToTable("vibe_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(256).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.LastMessageAt, c.CreatedAt });
    }
}
