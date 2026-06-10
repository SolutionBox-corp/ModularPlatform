using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Persistence.Encryption;

/// <summary>
/// Process-wide access point for the <see cref="IPersonalDataProtector"/> used by the DECRYPTING value
/// converter. A converter instance is baked into EF's cached MODEL (shared across context instances), so it
/// cannot take constructor dependencies — it reads the protector from here instead. Set once at host startup
/// by <see cref="PersonalDataEncryptionBootstrap"/>; null (e.g. design-time migrations, Gdpr module disabled)
/// makes reads pass ciphertext through untouched rather than crash.
/// </summary>
public static class PersonalDataEncryption
{
    public static volatile IPersonalDataProtector? Protector;

    /// <summary>True when the value is a protected envelope this platform wrote.</summary>
    public static bool LooksProtected(string value) =>
        value.StartsWith("penc:v", StringComparison.Ordinal);
}

/// <summary>
/// Hosted bootstrap that publishes the DI-resolved protector to <see cref="PersonalDataEncryption"/> before
/// any other hosted service (seeders, backfills) runs a query that needs decryption. Registered first by
/// <c>AddPlatformPersistence</c>; hosted services start in registration order.
/// </summary>
public sealed class PersonalDataEncryptionBootstrap(IServiceProvider services)
    : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        PersonalDataEncryption.Protector =
            (IPersonalDataProtector?)services.GetService(typeof(IPersonalDataProtector));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// READ-side converter applied by <see cref="PlatformDbContext"/> to every <c>[Encrypted]</c> string property:
/// materialization decrypts a protected envelope back to plaintext (shredded subject → the erased marker —
/// the ciphertext is permanently unreadable by design); legacy plaintext and erasure tombstones pass through.
/// The WRITE side is deliberately the identity function — encryption happens in
/// <see cref="PersonalDataEncryptionInterceptor"/>, which (unlike a converter) knows the row's subject.
/// </summary>
public sealed class PersonalDataDecryptingConverter() : ValueConverter<string, string>(
    model => model,
    provider => Reveal(provider))
{
    private static string Reveal(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !PersonalDataEncryption.LooksProtected(stored))
        {
            return stored;
        }

        var protector = PersonalDataEncryption.Protector;
        if (protector is null)
        {
            return stored; // No protector in this process (design-time/Gdpr off) — surface the raw envelope.
        }

        return protector.TryReveal(stored, out var plaintext)
            ? plaintext
            : PersonalDataProtection.ErasedMarker;
    }
}

/// <summary>
/// WRITE-side encryption for <c>[Encrypted]</c> columns. Runs AFTER the audit interceptor (registration order
/// in <c>AddModuleDbContext</c>), so audit still captures model-side plaintext and protects it itself; this
/// interceptor then seals the column value under the row subject's DEK via <see cref="IPersonalDataProtector"/>.
/// After a successful (or failed) save it restores the in-memory plaintext and re-syncs the tracker, so the
/// tracked instance stays usable and an unchanged property is never re-written.
/// <para>
/// Hard requirement: an <c>[Encrypted]</c> save without a protector (Gdpr module disabled) throws — silently
/// persisting plaintext would void the at-rest guarantee. <c>ExecuteUpdate/Delete</c> bypass this (documented).
/// </para>
/// </summary>
public sealed class PersonalDataEncryptionInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> EncryptedProps = new();
    private readonly ConditionalWeakTable<DbContext, List<PendingRestore>> _pending = new();

    private sealed record PendingRestore(EntityEntry Entry, string Property, string Plaintext);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        EncryptPending(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EncryptPending(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RestorePlaintext(eventData.Context, succeeded: true);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        RestorePlaintext(eventData.Context, succeeded: true);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RestorePlaintext(eventData.Context, succeeded: false);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RestorePlaintext(eventData.Context, succeeded: false);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void EncryptPending(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        List<PendingRestore>? restores = null;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Entity is not IDataSubject subject)
            {
                continue;
            }

            var props = EncryptedProps.GetOrAdd(entry.Entity.GetType(), static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                    && p.GetCustomAttribute<EncryptedAttribute>(inherit: true) is not null)
                .ToArray());

            foreach (var prop in props)
            {
                var property = entry.Property(prop.Name);
                if (entry.State == EntityState.Modified && !property.IsModified)
                {
                    continue;
                }

                if (property.CurrentValue is not string plaintext
                    || plaintext.Length == 0
                    || PersonalDataEncryption.LooksProtected(plaintext))
                {
                    continue;
                }

                var protector = (IPersonalDataProtector?)services.GetService(typeof(IPersonalDataProtector))
                    ?? throw new InvalidOperationException(
                        $"{entry.Entity.GetType().Name}.{prop.Name} is [Encrypted] but no IPersonalDataProtector "
                        + "is registered (is the Gdpr module enabled?). Refusing to persist PII in plaintext.");

                property.CurrentValue = protector.Protect(subject.SubjectId, plaintext);
                (restores ??= _pending.GetOrCreateValue(context)).Add(
                    new PendingRestore(entry, prop.Name, plaintext));
            }
        }
    }

    private void RestorePlaintext(DbContext? context, bool succeeded)
    {
        if (context is null || !_pending.TryGetValue(context, out var restores))
        {
            return;
        }

        _pending.Remove(context);

        foreach (var (entry, propertyName, plaintext) in restores)
        {
            var property = entry.Property(propertyName);
            property.CurrentValue = plaintext;
            if (succeeded)
            {
                // The row is committed; keep the tracker consistent with "the model value is plaintext" so an
                // unchanged property is not re-encrypted/re-written by a later save in the same scope.
                property.OriginalValue = plaintext;
                property.IsModified = false;
            }
        }
    }
}
