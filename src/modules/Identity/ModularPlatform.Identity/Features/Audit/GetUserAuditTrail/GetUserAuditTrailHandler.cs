using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Audit.GetUserAuditTrail;

/// <summary>
/// Reads the user's <c>identity_audit_entries</c> (no-tracking) and reveals personal-data columns through
/// <see cref="IPersonalDataProtector"/>: an envelope whose key is still live decrypts to plaintext; one whose key
/// has been shredded surfaces as <see cref="PersonalDataProtection.ErasedMarker"/>; non-PII values pass through.
/// </summary>
internal sealed class GetUserAuditTrailHandler(
    IReadDbContextFactory<IdentityDbContext> readFactory,
    IPersonalDataProtector protector,
    ITenantContext tenant)
    : IQueryHandler<GetUserAuditTrailQuery, UserAuditTrailResponse>
{
    public async Task<UserAuditTrailResponse> Handle(GetUserAuditTrailQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        if (!query.CrossTenant)
        {
            if (tenant.TenantId is not { } tenantId
                || !await db.Users
                    .IgnoreQueryFilters()
                    .AnyAsync(u => u.Id == query.UserId
                                   && EF.Property<Guid?>(u, "TenantId") == tenantId, ct))
            {
                throw new NotFoundException("user.not_found", "User not found.");
            }
        }

        var entityId = query.UserId.ToString();
        var rows = await db.AuditEntries
            .Where(a => a.EntityType == "User" && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new { a.Id, a.Action, a.Timestamp, a.NewValues })
            .ToListAsync(ct);

        var entries = rows
            .Select(r => new AuditTrailEntryResponse(r.Id, r.Action, r.Timestamp, Reveal(r.NewValues)))
            .ToList();

        return new UserAuditTrailResponse(entries);
    }

    private IReadOnlyDictionary<string, string?> Reveal(string newValuesJson)
    {
        var result = new Dictionary<string, string?>();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(newValuesJson) ? "{}" : newValuesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = RevealValue(prop.Value);
        }

        return result;
    }

    private string? RevealValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return value.ValueKind == JsonValueKind.Null ? null : value.GetRawText();
        }

        var raw = value.GetString()!;
        if (protector.TryReveal(raw, out var plaintext))
        {
            return plaintext;
        }

        return protector.IsProtected(raw) ? PersonalDataProtection.ErasedMarker : raw;
    }
}
