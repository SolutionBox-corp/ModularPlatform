using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Gdpr;

/// <summary>
/// GDPR data-portability for Identity: the subject's account profile (email, display name, locale, created).
/// Read-only via the read factory; the Gdpr module fans these exports into one document.
/// </summary>
internal sealed class IdentityPersonalDataExporter(IReadDbContextFactory<IdentityDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Identity";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var profile = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName, u.Locale, u.CreatedAt })
            .FirstOrDefaultAsync(ct);

        return new Dictionary<string, object?> { ["profile"] = profile };
    }
}
