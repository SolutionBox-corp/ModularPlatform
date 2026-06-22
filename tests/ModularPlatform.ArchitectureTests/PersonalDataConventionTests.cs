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
        typeof(ModularPlatform.Tenancy.TenancyModule).Assembly,
        typeof(ModularPlatform.Marketing.MarketingModule).Assembly,
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

    [Fact]
    public void Every_Encrypted_property_is_PersonalData_on_an_IDataSubject_string()
    {
        // [Encrypted] live-column protection rides the same DEK machinery as audit PII: the property must also
        // be [PersonalData] (audit never sees plaintext either way), must be a string (the envelope format),
        // and its entity must expose the subject (IDataSubject) the encryption interceptor seals under.
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<EncryptedAttribute>() is not null)
                .Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.PropertyType != typeof(string)
                || x.Property.GetCustomAttribute<PersonalDataAttribute>() is null
                || !typeof(IDataSubject).IsAssignableFrom(x.Type))
            .Select(x => $"{x.Type.FullName}.{x.Property.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"[Encrypted] requires: string property + [PersonalData] + IDataSubject entity. Offenders: {string.Join(", ", offenders)}");
    }

    private static bool HasPersonalDataProperty(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null);
}
