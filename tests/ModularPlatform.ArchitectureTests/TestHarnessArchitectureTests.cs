using System.Xml.Linq;

namespace ModularPlatform.ArchitectureTests;

public sealed class TestHarnessArchitectureTests
{
    private static readonly string[] ForbiddenModuleTestPackages =
    [
        "Microsoft.AspNetCore.Mvc.Testing",
        "Testcontainers",
        "Testcontainers.PostgreSql",
        "Testcontainers.MsSql",
    ];

    private static readonly string[] ForbiddenModuleTestSourceTokens =
    [
        "WebApplicationFactory<",
        "PostgreSqlContainer",
        "PostgreSqlBuilder",
        "MsSqlContainer",
        "ContainerBuilder",
        "Testcontainers.",
    ];

    [Fact]
    public void Module_test_projects_use_the_shared_integration_harness()
    {
        var violations = ModuleTestProjects()
            .Where(project => !ProjectReferencesIntegrationTesting(project))
            .Select(RelativeToRepo)
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Every module test project must reference tests/ModularPlatform.IntegrationTesting and reuse PlatformApiFactory: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void Module_test_projects_do_not_reference_testcontainers_or_web_application_factory_directly()
    {
        var violations = ModuleTestProjects()
            .Select(project => new
            {
                Project = project,
                Packages = PackageReferences(project)
                    .Where(package => ForbiddenModuleTestPackages.Any(forbidden =>
                        package.Equals(forbidden, StringComparison.Ordinal)
                        || package.StartsWith(forbidden + ".", StringComparison.Ordinal)))
                    .Order()
                    .ToList(),
            })
            .Where(item => item.Packages.Count > 0)
            .Select(item => $"{RelativeToRepo(item.Project)}: {string.Join(", ", item.Packages)}")
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Module test projects must not reference Testcontainers or WebApplicationFactory packages directly; use PlatformApiFactory from the shared harness: "
            + string.Join("; ", violations));
    }

    [Fact]
    public void Module_test_source_does_not_create_its_own_host_or_container()
    {
        var violations = ModuleTestSourceFiles()
            .Select(file => new
            {
                File = file,
                Source = StripComments(File.ReadAllText(file)),
            })
            .Where(item => ForbiddenModuleTestSourceTokens.Any(token => item.Source.Contains(token, StringComparison.Ordinal)))
            .Select(item => RelativeToRepo(item.File))
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Module tests must not create their own host/container; use PlatformApiFactory or fixture.CreateHost for same-DB derived hosts: "
            + string.Join(", ", violations));
    }

    private static IReadOnlyList<string> ModuleTestProjects()
    {
        var modulesDir = Path.Combine(FindRepoRoot(), "src", "modules");
        return Directory.GetFiles(modulesDir, "*.Tests.csproj", SearchOption.AllDirectories)
            .Order()
            .ToList();
    }

    private static IReadOnlyList<string> ModuleTestSourceFiles()
    {
        var modulesDir = Path.Combine(FindRepoRoot(), "src", "modules");
        return Directory.GetFiles(modulesDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order()
            .ToList();
    }

    private static bool ProjectReferencesIntegrationTesting(string project)
    {
        return XDocument.Load(project)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Any(value => value!.Contains("ModularPlatform.IntegrationTesting.csproj", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> PackageReferences(string project)
    {
        return XDocument.Load(project)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source,
            @"/\*.*?\*/",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return System.Text.RegularExpressions.Regex.Replace(
            withoutBlocks,
            @"^\s*//.*$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    private static string RelativeToRepo(string path) =>
        Path.GetRelativePath(FindRepoRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

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
