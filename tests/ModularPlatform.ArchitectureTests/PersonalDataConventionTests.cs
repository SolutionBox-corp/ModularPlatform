using System.Reflection;
using ModularPlatform.Abstractions;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// Enforces the personal-data pairing: any entity that marks a property <see cref="PersonalDataAttribute"/> MUST
/// implement <see cref="IDataSubject"/>, so the audit interceptor can resolve whose DEK protects that PII. Without
/// this, a `[PersonalData]` field on a non-subject entity would silently be REDACTED in the audit trail.
/// </summary>
public sealed class PersonalDataConventionTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(ModularPlatform.Identity.IdentityModule).Assembly,
        typeof(ModularPlatform.Billing.BillingModule).Assembly,
        typeof(ModularPlatform.Notifications.NotificationsModule).Assembly,
        typeof(ModularPlatform.Gdpr.GdprModule).Assembly,
        typeof(ModularPlatform.Operations.OperationsModule).Assembly,
        typeof(ModularPlatform.Files.FilesModule).Assembly,
    };

    [Fact]
    public void Every_type_with_a_PersonalData_property_implements_IDataSubject()
    {
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(HasPersonalDataProperty)
            .Where(t => !typeof(IDataSubject).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Types with a [PersonalData] property must implement IDataSubject. Offenders: {string.Join(", ", offenders)}");
    }

    private static bool HasPersonalDataProperty(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null);
}
