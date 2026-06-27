using System;
using System.Linq;
using System.Reflection;
using ModularPlatform.Persistence.Entities;
using Xunit;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// Enforces the RLS owner-column convention: <c>RlsBootstrapper</c> hardcodes the policy column name "UserId" for
/// every <see cref="IUserOwned"/> table. The marker interface itself has NO members, so an entity that implements it
/// with a differently-named ownership column (OwnerId/AuthorId/…) compiles fine but makes the bootstrapper emit DDL
/// against a non-existent column — a confusing STARTUP CRASH. This test catches it at build time instead.
/// </summary>
public sealed class RlsConventionTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(ModularPlatform.Identity.IdentityModule).Assembly,
        typeof(ModularPlatform.Billing.BillingModule).Assembly,
        typeof(ModularPlatform.Notifications.NotificationsModule).Assembly,
        typeof(ModularPlatform.Gdpr.GdprModule).Assembly,
        typeof(ModularPlatform.Operations.OperationsModule).Assembly,
        typeof(ModularPlatform.Files.FilesModule).Assembly,
        typeof(ModularPlatform.Crm.CrmModule).Assembly,
        typeof(ModularPlatform.Tenancy.TenancyModule).Assembly,
    };

    [Fact]
    public void Every_IUserOwned_entity_exposes_a_Guid_UserId()
    {
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IUserOwned).IsAssignableFrom(t))
            .Where(t =>
            {
                var prop = t.GetProperty("UserId", BindingFlags.Public | BindingFlags.Instance);
                return prop is null || prop.PropertyType != typeof(Guid);
            })
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(offenders.Count == 0,
            "IUserOwned entities must expose a public 'Guid UserId' (RlsBootstrapper's hardcoded policy column). "
            + "Offenders: " + string.Join(", ", offenders));
    }
}
