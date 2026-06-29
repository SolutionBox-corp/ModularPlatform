using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A B2B account/company owned by a user (per-user RLS), tenant-scoped, soft-deletable. Contacts and deals
/// reference it by <c>CompanyId</c> (no navigation, no cross-module JOIN). Name/Domain/Industry/Notes are plain text
/// but [PersonalData] so audited values crypto-shred under the user's DEK (the OWNER is the data subject, mirroring
/// the rest of CRM); the eraser scrubs the live row. Lookups are owner-scoped; Domain/Industry stay queryable.
/// </summary>
internal sealed class Company : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    [PersonalData]
    public string? Domain { get; set; }

    [PersonalData]
    public string? Industry { get; set; }

    [PersonalData]
    public string? Notes { get; set; }

    Guid IDataSubject.SubjectId => UserId;

    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("crm_companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Domain).HasMaxLength(256);
        builder.Property(c => c.Industry).HasMaxLength(128);
        builder.Property(c => c.Notes).HasMaxLength(8192);

        builder.HasIndex(c => new { c.UserId, c.CreatedAt });
        builder.HasIndex(c => new { c.UserId, c.Industry });
    }
}
