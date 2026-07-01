using System.Text.RegularExpressions;

namespace ModularPlatform.ArchitectureTests;

public sealed class ProductionDeployConfigurationTests
{
    [Fact]
    public void Production_compose_and_runbook_use_one_database_and_one_frontend_port()
    {
        var root = FindRepoRoot();
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        var env = File.ReadAllText(Path.Combine(root, "docs/deploy/.env.example"));
        var runbook = File.ReadAllText(Path.Combine(root, "docs/deploy-solutionbox2.md"));
        var nginx = File.ReadAllText(Path.Combine(root, "docs/deploy/nginx-mp.solutionbox.cz.conf"));

        Assert.Contains("postgres:", compose);
        Assert.Contains("ConnectionStrings__Write=Host=postgres;", env);
        Assert.Contains("ConnectionStrings__Read=Host=postgres;", env);
        Assert.Contains("POSTGRES_PASSWORD=", env);
        Assert.DoesNotContain("ConnectionStrings__Write=Host=host.docker.internal", env);
        Assert.DoesNotContain("Host PostgreSQL", runbook);

        var composePort = Regex.Match(compose, "\"127\\.0\\.0\\.1:(?<port>\\d+):3000\"");
        Assert.True(composePort.Success, "docker-compose.yml must publish the web service on a loopback host port.");

        var port = composePort.Groups["port"].Value;
        Assert.Contains($"127.0.0.1:{port}", nginx);
        Assert.Contains($"127.0.0.1:{port}", runbook);
        Assert.DoesNotContain("16010", compose);
        Assert.DoesNotContain("16010", runbook);

        Assert.Contains("ForwardedHeaders__KnownNetworks__0=172.16.0.0/12", env);
    }

    [Fact]
    public void Production_smoke_script_guards_the_deploy_invariants()
    {
        var root = FindRepoRoot();
        var smoke = File.ReadAllText(Path.Combine(root, "docs/deploy/production-smoke.sh"));

        Assert.Contains("ConnectionStrings__Write=Host=postgres;", smoke);
        Assert.Contains("docker compose run --rm migrator", smoke);
        Assert.Contains("http://localhost:8080/health/ready", smoke);
        Assert.Contains("http://127.0.0.1:16013/", smoke);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
