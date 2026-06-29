namespace ModularPlatform.ArchitectureTests;

public sealed class HostMessagingArchitectureTests
{
    [Fact]
    public void Api_host_listens_to_wolverine_queue_only_when_solo_mode_is_enabled()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "hosts", "ModularPlatform.Api", "Program.cs"));

        Assert.Contains(
            "var soloMode = builder.Configuration.GetValue(\"Messaging:SoloMode\", builder.Environment.IsEnvironment(\"Testing\"));",
            source);
        Assert.Contains(
            "builder.UseWolverine(opts => PlatformMessaging.Configure(opts, writeConn, modules, soloMode, listen: soloMode));",
            source);
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
