using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Persistence.Audit;

/// <summary>
/// Stamps audit fields (Created/Updated by/at) and writes one <see cref="AuditEntry"/> per change,
/// capturing only changed columns on updates. Registered scoped so it sees the current
/// <see cref="ITenantContext"/> and <see cref="IClock"/>.
/// </summary>
public sealed class AuditInterceptor(IClock clock, ITenantContext tenant) : SaveChangesInterceptor
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

    private static AuditEntry BuildAuditEntry(
        EntityEntry entry, DateTimeOffset now, Guid? userId, Guid? tenantId, string? ip)
    {
        var (action, changedColumns, newValues) = entry.State switch
        {
            EntityState.Added => ("Create", AllColumns(entry), CurrentValues(entry)),
            EntityState.Deleted => ("Delete", AllColumns(entry), CurrentValues(entry)),
            _ => ("Update", ChangedColumns(entry), ChangedValues(entry)),
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

    private static Dictionary<string, object?> CurrentValues(EntityEntry entry) =>
        entry.Properties.Where(p => !p.Metadata.IsPrimaryKey())
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    private static Dictionary<string, object?> ChangedValues(EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
}
