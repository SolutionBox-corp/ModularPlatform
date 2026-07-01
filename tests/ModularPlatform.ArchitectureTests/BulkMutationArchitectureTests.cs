using System.Text.RegularExpressions;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// EF set-based bulk writes bypass SaveChanges interceptors (audit, live-column encryption) and xmin concurrency.
/// They are allowed only for explicitly reviewed cases: GDPR scrubs, non-PII maintenance purges, the billing
/// atomic debit guard, and a few module-local bulk state changes whose audit tradeoff is documented in code.
/// </summary>
public sealed class BulkMutationArchitectureTests
{
    private static readonly Regex BulkMutationCall = new(@"\.Execute(?:Update|Delete)Async\s*\(", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, int> AllowedBulkMutationCounts = new Dictionary<string, int>
    {
        // GDPR scrubs / erasures: intentionally bypass audit/encryption because retained audit PII is crypto-shredded
        // and live rows are being erased or removed.
        ["src/modules/Crm/ModularPlatform.Crm/Gdpr/CrmPersonalDataEraser.cs"] = 10,
        ["src/modules/Files/ModularPlatform.Files/Gdpr/FilesPersonalDataEraser.cs"] = 2,
        ["src/modules/Gdpr/ModularPlatform.Gdpr/Features/Consents/ConsentPersonalDataEraser.cs"] = 1,
        ["src/modules/Identity/ModularPlatform.Identity/Gdpr/IdentityPersonalDataEraser.cs"] = 2,
        ["src/modules/Marketing/ModularPlatform.Marketing/Gdpr/MarketingPersonalDataEraser.cs"] = 5,
        ["src/modules/Notifications/ModularPlatform.Notifications/Gdpr/NotificationsPersonalDataEraser.cs"] = 1,

        // Non-PII maintenance purge; expired refresh tokens are removed after the retention window.
        ["src/modules/Identity/ModularPlatform.Identity/Features/Auth/PurgeRefreshTokens/PurgeRefreshTokensHandler.cs"] = 1,

        // Money debit guard: atomic conditional update is the correctness mechanism; credit_entries are the audit.
        ["src/modules/Billing/ModularPlatform.Billing/Features/Credits/ReserveCredits/ReserveCreditsHandler.cs"] = 1,

        // Reviewed module-local bulk state changes. Each handler wraps related tracked/bulk work in a transaction or
        // documents why the read-flag flip can bypass audit/xmin.
        ["src/modules/Crm/ModularPlatform.Crm/Features/Companies/DeleteCompany/DeleteCompanyHandler.cs"] = 2,
        ["src/modules/Crm/ModularPlatform.Crm/Features/Kanban/DeleteBoard/DeleteBoardHandler.cs"] = 2,
        ["src/modules/Notifications/ModularPlatform.Notifications/Features/Notifications/MarkAllRead/MarkAllReadHandler.cs"] = 1,
    };

    [Fact]
    public void ExecuteUpdate_and_ExecuteDelete_are_allowed_only_in_reviewed_locations()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        var actual = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => new
            {
                Path = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                Count = BulkMutationCall.Matches(File.ReadAllText(file)).Count,
            })
            .Where(item => item.Count > 0)
            .OrderBy(item => item.Path)
            .ToList();

        var unexpected = actual
            .Where(item => !AllowedBulkMutationCounts.TryGetValue(item.Path, out var expected) || expected != item.Count)
            .Select(item => $"{item.Path} has {item.Count} bulk mutation call(s)")
            .ToList();

        var missing = AllowedBulkMutationCounts
            .Where(allowed => actual.All(item => item.Path != allowed.Key))
            .Select(allowed => $"{allowed.Key} expected {allowed.Value} bulk mutation call(s)")
            .ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            "ExecuteUpdateAsync/ExecuteDeleteAsync bypass audit, encryption and xmin. "
            + "Add new usage only after documenting why the bypass is safe and updating this allowlist. "
            + "Unexpected: " + string.Join("; ", unexpected)
            + ". Missing: " + string.Join("; ", missing));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ModularPlatform.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root (ModularPlatform.slnx) not found.");
    }
}
