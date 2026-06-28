using System.Text.RegularExpressions;

namespace ModularPlatform.ArchitectureTests;

public sealed class FrontendArchitectureTests
{
    [Fact]
    public void Feature_components_do_not_call_backend_clients_directly()
    {
        var componentFiles = FindFrontendFiles("features/*/components/*.{ts,tsx}");
        var violations = componentFiles
            .Select(file => new
            {
                File = file,
                Source = StripComments(File.ReadAllText(file)),
            })
            .Where(item =>
                Regex.IsMatch(item.Source, @"\bfetch\s*\(")
                || Regex.IsMatch(item.Source, @"\bapiFetch\s*(<[^>]+>)?\s*\(")
                || item.Source.Contains("/api/bff/", StringComparison.Ordinal)
                || item.Source.Contains("/v1/", StringComparison.Ordinal))
            .Select(item => RelativeToRepo(item.File))
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Feature components must use feature hooks/actions instead of direct backend calls: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void Feature_api_files_use_bff_relative_api_fetch_paths()
    {
        var apiFiles = FindFrontendFiles("features/*/api.ts");
        var violations = apiFiles
            .Select(file => new
            {
                File = file,
                Source = StripComments(File.ReadAllText(file)),
            })
            .Where(item =>
                Regex.IsMatch(item.Source, @"\bapiFetch\s*(<[^>]+>)?\s*\(\s*[""'`]/")
                || Regex.IsMatch(item.Source, @"\bapiFetch\s*(<[^>]+>)?\s*\(\s*[""'`]https?://")
                || Regex.IsMatch(item.Source, @"\bapiFetch\s*(<[^>]+>)?\s*\(\s*[""'`][^""'`]*?/v1/"))
            .Select(item => RelativeToRepo(item.File))
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Feature api.ts files must call apiFetch with BFF-relative paths like \"marketing/pulls\", not /v1 or absolute URLs: "
            + string.Join(", ", violations));
    }

    private static IReadOnlyList<string> FindFrontendFiles(string glob)
    {
        var frontend = Path.Combine(FindRepoRoot(), "frontend");
        return Directory.GetFiles(frontend, "*", SearchOption.AllDirectories)
            .Where(path => GlobMatches(frontend, glob, path))
            .Order()
            .ToList();
    }

    private static bool GlobMatches(string frontendRoot, string glob, string path)
    {
        var relative = Path.GetRelativePath(frontendRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return glob switch
        {
            "features/*/components/*.{ts,tsx}" =>
                Regex.IsMatch(relative, @"^features/[^/]+/components/[^/]+\.(ts|tsx)$"),
            "features/*/api.ts" =>
                Regex.IsMatch(relative, @"^features/[^/]+/api\.ts$"),
            _ => throw new ArgumentOutOfRangeException(nameof(glob), glob, "Unsupported frontend test glob."),
        };
    }

    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "frontend"))
                && File.Exists(Path.Combine(dir.FullName, "ModularPlatform.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find ModularPlatform repo root from test output path.");
    }

    private static string RelativeToRepo(string path)
    {
        return Path.GetRelativePath(FindRepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
