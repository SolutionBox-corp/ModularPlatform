using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A revenue opportunity owned by a user (per-user RLS), tenant-scoped, soft-deletable. May reference a
/// <see cref="Contact"/> by Id (optional). Amount is stored in minor units (cents) to avoid float money error;
/// Currency is an ISO-4217 code. Lifecycle is a text <see cref="Stage"/>: lead → qualified → proposal →
/// negotiation → won | lost (won/lost are terminal, set <see cref="ClosedAt"/>). Notes is free text, [PersonalData]
/// (audit crypto-shred) AND [Encrypted] at rest under the user's DEK; it is not a list filter target.
/// </summary>
internal sealed class Deal : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? CompanyId { get; set; }

    public string Title { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "USD";

    public string Stage { get; set; } = DealStages.Lead;
    public string? LastStage { get; set; }
    public int ProbabilityPercent { get; set; } = 10;
    public string? LeadSource { get; set; }
    public DateTimeOffset? ExpectedCloseAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    [PersonalData]
    [Encrypted]
    public string? Notes { get; set; }

    [PersonalData]
    [Encrypted]
    public string? NextStep { get; set; }

    Guid IDataSubject.SubjectId => UserId;

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>The allowed <see cref="Deal.Stage"/> values; won/lost are terminal.</summary>
internal static class DealStages
{
    public const string Lead = "lead";
    public const string Qualified = "qualified";
    public const string Proposal = "proposal";
    public const string Negotiation = "negotiation";
    public const string Won = "won";
    public const string Lost = "lost";

    public static readonly string[] All = [Lead, Qualified, Proposal, Negotiation, Won, Lost];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
    public static bool IsTerminal(string stage) => stage is Won or Lost;
    public static int DefaultProbability(string stage) => stage switch
    {
        Lead => 10,
        Qualified => 25,
        Proposal => 50,
        Negotiation => 75,
        Won => 100,
        Lost => 0,
        _ => 10,
    };
}

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> builder)
    {
        builder.ToTable("crm_deals");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Currency).HasMaxLength(3).IsRequired();
        builder.Property(d => d.Stage).HasMaxLength(32).IsRequired();
        builder.Property(d => d.LastStage).HasMaxLength(32);
        builder.Property(d => d.LeadSource).HasMaxLength(64);
        // Encrypted at rest (penc:v2 envelope) → unbounded text; plaintext length bounded by the validator (8192).
        builder.Property(d => d.Notes);
        builder.Property(d => d.NextStep);

        builder.HasIndex(d => new { d.UserId, d.Stage });
        builder.HasIndex(d => new { d.UserId, d.LeadSource });
        builder.HasIndex(d => new { d.UserId, d.CreatedAt });
        builder.HasIndex(d => d.ContactId);
        builder.HasIndex(d => d.CompanyId);
    }
}
