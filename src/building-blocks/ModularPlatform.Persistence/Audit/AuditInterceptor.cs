using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Persistence.Audit;

/// <summary>
/// Stamps audit fields (Created/Updated by/at) and writes one <see cref="AuditEntry"/> per change,
/// capturing only changed columns on updates. Registered as a singleton; it reads the current
/// <see cref="ITenantContext"/> and <see cref="IClock"/> live (both singleton, request-aware).
/// <para>
/// Properties marked <see cref="PersonalDataAttribute"/> are crypto-shredded before they reach the audit JSON:
/// their value is replaced with an opaque envelope from <see cref="IPersonalDataProtector"/> (keyed by the
/// entity's <see cref="IDataSubject.SubjectId"/>), so GDPR erasure (shred the DEK) makes audit PII unrecoverable.
/// If no protector is wired (e.g. Gdpr module disabled) or the subject can't be resolved, the PII value is
/// REDACTED — it is never written in clear.
/// </para>
/// </summary>
public sealed class AuditInterceptor(IClock clock, ITenantContext tenant, IPersonalDataProtector? protector = null)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = clock.UtcNow;
        var userId = tenant.UserId;
        var tenantId = tenant.TenantId;
        var ip = tenant.IpAddress;

        // Snapshot the audited entries BEFORE we add audit rows (so we don't audit the audit).
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var auditRows = new List<AuditEntry>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = userId;
                }
            }

            auditRows.Add(BuildAuditEntry(entry, now, userId, tenantId, ip));
        }

        if (auditRows.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(auditRows);
        }
    }

    private AuditEntry BuildAuditEntry(
        EntityEntry entry, DateTimeOffset now, Guid? userId, Guid? tenantId, string? ip)
    {
        // PII fields are crypto-shredded under THIS entity's data subject (its own Id for a user, UserId for an
        // owned entity). Resolved once; null when the entity is not an IDataSubject -> PII is redacted.
        var subjectId = (entry.Entity as IDataSubject)?.SubjectId;

        var (action, changedColumns, newValues) = entry.State switch
        {
            EntityState.Added => ("Create", AllColumns(entry), ValueMap(entry, changedOnly: false, subjectId)),
            EntityState.Deleted => ("Delete", AllColumns(entry), ValueMap(entry, changedOnly: false, subjectId)),
            _ => ("Update", ChangedColumns(entry), ValueMap(entry, changedOnly: true, subjectId)),
        };

        return new AuditEntry
        {
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = PrimaryKey(entry),
            Action = action,
            ChangedColumns = JsonSerializer.Serialize(changedColumns),
            NewValues = JsonSerializer.Serialize(newValues),
            UserId = userId,
            TenantId = tenantId,
            IpAddress = ip,
            Timestamp = now,
        };
    }

    private static string PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return string.Empty;
        }

        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "");
        return string.Join(",", values);
    }

    private static List<string> AllColumns(EntityEntry entry) =>
        entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()).Select(p => p.Metadata.Name).ToList();

    private static List<string> ChangedColumns(EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
            .Select(p => p.Metadata.Name).ToList();

    private Dictionary<string, object?> ValueMap(EntityEntry entry, bool changedOnly, Guid? subjectId) =>
        entry.Properties
            .Where(p => !p.Metadata.IsPrimaryKey() && (!changedOnly || p.IsModified))
            .ToDictionary(p => p.Metadata.Name, p => AuditValue(p, subjectId));

    /// <summary>
    /// The value to record for one property. Non-PII: the STORED value (EF value converter applied — so a
    /// <c>HasConversion&lt;string&gt;()</c> enum is "Confirmed", not 1). PII (<see cref="PersonalDataAttribute"/>):
    /// the crypto-shredded envelope from the protector, or a redaction marker — NEVER the plaintext.
    /// </summary>
    private object? AuditValue(PropertyEntry property, Guid? subjectId)
    {
        var stored = ProviderValue(property);

        if (!IsPersonalData(property))
        {
            return stored;
        }

        if (stored is null)
        {
            return null;
        }

        var plaintext = stored.ToString() ?? string.Empty;
        // Require a real subject: an empty subject id could not be erased (its DEK would never be shredded), so
        // redact rather than encrypt PII under a subject the erasure path can't reach.
        return protector is not null && subjectId is { } sid && sid != Guid.Empty
            ? protector.Protect(sid, plaintext)
            : PersonalDataProtection.RedactedMarker;
    }

    private static bool IsPersonalData(PropertyEntry property) =>
        property.Metadata.PropertyInfo?.GetCustomAttribute<PersonalDataAttribute>(inherit: true) is not null;

    private static object? ProviderValue(PropertyEntry property)
    {
        var value = property.CurrentValue;
        var converter = property.Metadata.GetValueConverter();
        return converter is null || value is null ? value : converter.ConvertToProvider(value);
    }
}
