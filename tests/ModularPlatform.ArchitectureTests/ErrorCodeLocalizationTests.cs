using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// i18n contract (CLAUDE.md §8): every user-facing error code thrown in the codebase MUST have a resx entry in
/// BOTH en (<c>SharedResource.resx</c>) and cs (<c>SharedResource.cs.resx</c>) — the GlobalExceptionMiddleware
/// localizes the RFC 9457 <c>detail</c> from a resx whose key == the errorCode. Without this guard, codes silently
/// slipped through with no translation (the detail fell back to the raw English message).
/// </summary>
public sealed class ErrorCodeLocalizationTests
{
    private static readonly Regex CodeUsage = new(
        @"(?:Exception|WithErrorCode)\(\s*""([a-z0-9_]+(?:\.[a-z0-9_]+)+)""", RegexOptions.Compiled);

    private static readonly Regex ResxKey = new(@"<data name=""([a-z0-9_.]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_thrown_error_code_is_localized_in_en_and_cs()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");

        var usedCodes = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => CodeUsage.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet();

        var en = ResxKeys(Path.Combine(srcDir,
            "building-blocks", "ModularPlatform.Web", "Localization", "SharedResource.resx"));
        var cs = ResxKeys(Path.Combine(srcDir,
            "building-blocks", "ModularPlatform.Web", "Localization", "SharedResource.cs.resx"));

        var missingEn = usedCodes.Where(c => !en.Contains(c)).OrderBy(c => c).ToList();
        var missingCs = usedCodes.Where(c => !cs.Contains(c)).OrderBy(c => c).ToList();

        Assert.True(missingEn.Count == 0, "Error codes thrown but missing from SharedResource.resx (en): "
            + string.Join(", ", missingEn));
        Assert.True(missingCs.Count == 0, "Error codes thrown but missing from SharedResource.cs.resx (cs): "
            + string.Join(", ", missingCs));
    }

    private static HashSet<string> ResxKeys(string path) =>
        ResxKey.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value).ToHashSet();

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
