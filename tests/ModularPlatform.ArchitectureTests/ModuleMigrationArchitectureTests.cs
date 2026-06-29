using System.Reflection;
using Microsoft.EntityFrameworkCore.Design;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;

namespace ModularPlatform.ArchitectureTests;

public sealed class ModuleMigrationArchitectureTests
{
    private static readonly Assembly[] ModuleCoreAssemblies =
    [
        typeof(ModularPlatform.Identity.IdentityModule).Assembly,
        typeof(ModularPlatform.Billing.BillingModule).Assembly,
        typeof(ModularPlatform.Notifications.NotificationsModule).Assembly,
        typeof(ModularPlatform.Gdpr.GdprModule).Assembly,
        typeof(ModularPlatform.Operations.OperationsModule).Assembly,
        typeof(ModularPlatform.Files.FilesModule).Assembly,
        typeof(ModularPlatform.Marketing.MarketingModule).Assembly,
        typeof(ModularPlatform.Tenancy.TenancyModule).Assembly,
    ];

    [Fact]
    public void Every_module_db_context_has_a_design_time_factory()
    {
        var missing = ModuleDbContexts()
            .Where(context => !HasDesignTimeFactory(context))
            .Select(context => context.FullName)
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Every module DbContext needs an IDesignTimeDbContextFactory<TContext> so dotnet ef migrations add works: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Design_time_factories_use_system_tenant_context()
    {
        var violations = ModuleDbContexts()
            .Select(context => new
            {
                Context = context,
                Factory = FindDesignTimeFactory(context),
            })
            .Where(item => item.Factory is not null)
            .Where(item => !File.ReadAllText(SourcePath(item.Factory!))
                .Contains("new SystemTenantContext()", StringComparison.Ordinal))
            .Select(item => item.Factory!.FullName)
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Design-time factories must create DbContexts with SystemTenantContext, not a fake user/tenant: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void Module_migration_hooks_apply_their_own_context_with_platform_migrator()
    {
        var violations = ModuleDbContexts()
            .Select(context => new
            {
                Context = context,
                Module = FindModule(context.Assembly),
            })
            .Where(item => item.Module is null || !ModuleSourceMigratesContext(item.Module, item.Context))
            .Select(item => item.Module is null
                ? $"{item.Context.FullName}: no IModule in assembly"
                : $"{item.Module.FullName}: missing PlatformMigrator.MigrateAsync<{item.Context.Name}>")
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Modules with a DbContext must override ApplyMigrationsAsync and call PlatformMigrator.MigrateAsync<TContext> on the admin connection: "
            + string.Join(", ", violations));
    }

    private static IReadOnlyList<Type> ModuleDbContexts() =>
        ModuleCoreAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(PlatformDbContext).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToList();

    private static bool HasDesignTimeFactory(Type context) =>
        FindDesignTimeFactory(context) is not null;

    private static Type? FindDesignTimeFactory(Type context)
    {
        var factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(context);
        return context.Assembly.GetTypes()
            .SingleOrDefault(type => !type.IsAbstract && factoryInterface.IsAssignableFrom(type));
    }

    private static Type? FindModule(Assembly assembly) =>
        assembly.GetTypes().SingleOrDefault(type => !type.IsAbstract && typeof(IModule).IsAssignableFrom(type));

    private static bool ModuleSourceMigratesContext(Type? module, Type context)
    {
        if (module is null)
        {
            return false;
        }

        var source = File.ReadAllText(SourcePath(module));
        return source.Contains("ApplyMigrationsAsync", StringComparison.Ordinal)
            && source.Contains("PlatformMigrator.MigrateAsync", StringComparison.Ordinal)
            && source.Contains($"MigrateAsync<{context.Name}>", StringComparison.Ordinal);
    }

    private static string SourcePath(Type type)
    {
        var repoRoot = FindRepoRoot();
        var relativeNamespace = type.Namespace!.Replace("ModularPlatform.", string.Empty).Replace('.', Path.DirectorySeparatorChar);
        var moduleName = type.Assembly.GetName().Name!.Split('.')[1];
        var moduleRoot = Path.Combine(repoRoot, "src", "modules", moduleName, $"ModularPlatform.{moduleName}");
        var candidates = Directory.GetFiles(moduleRoot, $"{type.Name}.cs", SearchOption.AllDirectories);

        return candidates.SingleOrDefault()
            ?? throw new FileNotFoundException($"Could not find source for {type.FullName} under {moduleRoot}. Namespace hint: {relativeNamespace}");
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
