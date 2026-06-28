using System.Text.RegularExpressions;

namespace ModularPlatform.ArchitectureTests;

public sealed class FrontendArchitectureTests
{
    private static readonly Regex FrontendErrorCodeLiteral = new(
        @"(?:errorCode\s*:\s*|\.errorCode\s*={2,3}\s*)[""']([a-z0-9_]+(?:\.[a-z0-9_]+)+)[""']",
        RegexOptions.Compiled);

    private static readonly Regex FrontendErrorCatalogKey = new(
        @"[""']([a-z0-9_]+(?:\.[a-z0-9_]+)+)[""']\s*:",
        RegexOptions.Compiled);

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

    [Fact]
    public void Feature_mutation_hooks_declare_cache_effects()
    {
        var hookFiles = FindFrontendFiles("features/*/hooks.ts");
        var violations = hookFiles
            .SelectMany(file => FindExportedHookFunctions(file)
                .Where(hook => hook.Source.Contains("useMutation", StringComparison.Ordinal))
                .Where(hook => !DeclaresMutationCacheEffect(hook.Source))
                .Select(hook => $"{RelativeToRepo(file)}::{hook.Name}"))
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Feature useMutation hooks must invalidate/remove/clear/set query cache, redirect away, or explicitly document why no invalidation is needed: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void Realtime_stream_is_owned_by_the_central_provider()
    {
        const string provider = "frontend/lib/realtime/realtime-provider.tsx";
        var violations = FindFrontendSourceFiles()
            .Where(file => RelativeToRepo(file) != provider)
            .Select(file => new
            {
                File = file,
                Source = StripComments(File.ReadAllText(file)),
            })
            .Where(item =>
                Regex.IsMatch(item.Source, @"\b(new\s+)?EventSource(Plus)?\b")
                || Regex.IsMatch(item.Source, @"\bnew\s+WebSocket\s*\(")
                || item.Source.Contains("/api/bff/realtime/stream", StringComparison.Ordinal)
                || item.Source.Contains("/v1/realtime/stream", StringComparison.Ordinal))
            .Select(item => RelativeToRepo(item.File))
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Realtime SSE/WebSocket ownership must stay in frontend/lib/realtime/realtime-provider.tsx; modules add event-map rows and invalidate queries instead: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void Frontend_error_code_literals_are_in_the_display_catalog()
    {
        var catalogPath = Path.Combine(FindRepoRoot(), "frontend", "lib", "errors", "error-map.ts");
        var catalogCodes = FrontendErrorCatalogKey
            .Matches(File.ReadAllText(catalogPath))
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

        var usedCodes = FindFrontendSourceFiles()
            .SelectMany(file => FrontendErrorCodeLiteral
                .Matches(StripComments(File.ReadAllText(file)))
                .Select(match => new
                {
                    Code = match.Groups[1].Value,
                    File = RelativeToRepo(file),
                }))
            .Where(item => item.File != "frontend/lib/errors/error-map.ts")
            .GroupBy(item => item.Code)
            .Where(group => !catalogCodes.Contains(group.Key))
            .Select(group => $"{group.Key} ({string.Join(", ", group.Select(item => item.File).Distinct().Order())})")
            .Order()
            .ToList();

        Assert.True(
            usedCodes.Count == 0,
            "Frontend errorCode literals must have EN/CS fallback entries in frontend/lib/errors/error-map.ts: "
            + string.Join("; ", usedCodes));
    }

    private static IReadOnlyList<string> FindFrontendFiles(string glob)
    {
        var frontend = Path.Combine(FindRepoRoot(), "frontend");
        return Directory.GetFiles(frontend, "*", SearchOption.AllDirectories)
            .Where(path => GlobMatches(frontend, glob, path))
            .Order()
            .ToList();
    }

    private static IReadOnlyList<string> FindFrontendSourceFiles()
    {
        var frontend = Path.Combine(FindRepoRoot(), "frontend");
        return Directory.GetFiles(frontend, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.next{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}playwright-report{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}test-results{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
            "features/*/hooks.ts" =>
                Regex.IsMatch(relative, @"^features/[^/]+/hooks\.ts$"),
            _ => throw new ArgumentOutOfRangeException(nameof(glob), glob, "Unsupported frontend test glob."),
        };
    }

    private static IReadOnlyList<ExportedHook> FindExportedHookFunctions(string file)
    {
        var source = File.ReadAllText(file);
        var matches = Regex.Matches(source, @"export function (use[A-Za-z0-9_]+)\s*\(");
        var hooks = new List<ExportedHook>();

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
            hooks.Add(new ExportedHook(
                matches[i].Groups[1].Value,
                source[start..end]));
        }

        return hooks;
    }

    private static bool DeclaresMutationCacheEffect(string hookSource)
    {
        return hookSource.Contains("invalidateQueries", StringComparison.Ordinal)
            || hookSource.Contains("removeQueries", StringComparison.Ordinal)
            || Regex.IsMatch(hookSource, @"\b(setQueryData|clear)\s*\(")
            || hookSource.Contains("safeExternalRedirect", StringComparison.Ordinal)
            || hookSource.Contains("No invalidation needed", StringComparison.Ordinal);
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

    private sealed record ExportedHook(string Name, string Source);
}
