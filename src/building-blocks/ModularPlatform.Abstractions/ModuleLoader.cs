using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ModularPlatform.Abstractions;

/// <summary>
/// Discovers <see cref="IModule"/> implementations across the given assemblies and gates each on
/// <c>Modules:{Name}:Enabled</c> (default true). Every host (Api/Worker/Jobs/Migration) instantiates
/// the modules ONCE and shares the same list for service registration, endpoint mapping AND Wolverine
/// configuration — so a module is never half-wired across hosts.
/// </summary>
public static class ModuleLoader
{
    public static IReadOnlyList<IModule> Discover(IConfiguration configuration, params Assembly[] assemblies)
    {
        var modules = new List<IModule>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type is { IsAbstract: false, IsInterface: false } && typeof(IModule).IsAssignableFrom(type))
                {
                    var module = (IModule)Activator.CreateInstance(type)!;
                    var enabled = configuration.GetValue<bool?>($"Modules:{module.Name}:Enabled") ?? true;
                    if (enabled)
                    {
                        modules.Add(module);
                    }
                }
            }
        }

        return modules;
    }
}
