using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A B2B account/company owned by a user (per-user RLS), tenant-scoped, soft-deletable. Contacts and deals
/// reference it by <c>CompanyId</c> (no navigation, no cross-module JOIN). Company profile fields are plain text but
/// [PersonalData] so audited values crypto-shred under the user's DEK (the OWNER is the data subject, mirroring the
/// rest of CRM); the eraser scrubs the live row. Lookups are owner-scoped; Domain/Industry stay queryable.
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

    public string Type { get; set; } = CompanyTypes.Prospect;

    [PersonalData]
    public string? IdentificationNumber { get; set; }

    [PersonalData]
    public string? TaxIdentificationNumber { get; set; }

    [PersonalData]
    public string? RegisteredAddress { get; set; }

    [PersonalData]
    public string? City { get; set; }

    [PersonalData]
    public string? PostalCode { get; set; }

    [PersonalData]
    public string? Country { get; set; }

    [PersonalData]
    public string? Notes { get; set; }

    Guid IDataSubject.SubjectId => UserId;

    public DateTimeOffset? DeletedAt { get; set; }
}

internal static class CompanyTypes
{
    public const string Prospect = "prospect";
    public const string Customer = "customer";
    public const string Partner = "partner";
    public const string Reseller = "reseller";
    public const string Vendor = "vendor";

    public static readonly string[] All = [Prospect, Customer, Partner, Reseller, Vendor];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
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
        builder.Property(c => c.Type).HasMaxLength(32).IsRequired();
        builder.Property(c => c.IdentificationNumber).HasMaxLength(32);
        builder.Property(c => c.TaxIdentificationNumber).HasMaxLength(32);
        builder.Property(c => c.RegisteredAddress).HasMaxLength(512);
        builder.Property(c => c.City).HasMaxLength(128);
        builder.Property(c => c.PostalCode).HasMaxLength(32);
        builder.Property(c => c.Country).HasMaxLength(128);
        builder.Property(c => c.Notes).HasMaxLength(8192);

        builder.HasIndex(c => new { c.UserId, c.CreatedAt });
        builder.HasIndex(c => new { c.UserId, c.Industry });
        builder.HasIndex(c => new { c.UserId, c.Type });
    }
}
