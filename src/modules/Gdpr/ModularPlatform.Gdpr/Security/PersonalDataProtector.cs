using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Gdpr.Entities;
using ModularPlatform.Gdpr.Persistence;

namespace ModularPlatform.Gdpr.Security;

/// <summary>
/// Gdpr's implementation of <see cref="IPersonalDataProtector"/>: encrypts audit PII under each subject's DEK (the
/// <see cref="SubjectKey"/> envelope) via <see cref="CryptoShredder"/>. The envelope is
/// <c>penc:v1:{base64(subjectId)}:{base64(nonce|tag|ciphertext)}</c>.
/// <para>
/// It runs on its OWN short-lived <see cref="GdprDbContext"/> (system tenant context, NO audit interceptor) supplied
/// by <paramref name="contextFactory"/>, so it is safe to call from inside the audit interceptor (no reentrancy into
/// the audited context) and may read/write <c>subject_keys</c> for any subject (RLS bypassed for system).
/// </para>
/// BOTH encrypt and decrypt read the DEK LIVE from the DB on every call and refuse a shredded/missing key
/// (<c>WrappedDek</c> null OR <c>DeletedAt</c> set). The DEK is therefore never retained in process memory beyond a
/// single call, and a post-erasure encrypt cleanly redacts instead of re-using a destroyed key — the crypto-shred
/// guarantee holds even across processes (Api caches nothing the Worker's shred can't immediately defeat).
/// </summary>
internal sealed class PersonalDataProtector(Func<GdprDbContext> contextFactory, IClock clock) : IPersonalDataProtector
{
    private const string Prefix = "penc:v1:";

    public bool IsProtected(string value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(Guid subjectId, string plaintext)
    {
        var dek = GetOrCreateDek(subjectId);
        if (dek is null)
        {
            // The subject's key is already shredded — never write plaintext, redact instead.
            return PersonalDataProtection.RedactedMarker;
        }

        var blob = CryptoShredder.Encrypt(Encoding.UTF8.GetBytes(plaintext), dek);
        return $"{Prefix}{Convert.ToBase64String(subjectId.ToByteArray())}:{Convert.ToBase64String(blob)}";
    }

    public bool TryReveal(string value, out string plaintext)
    {
        plaintext = string.Empty;
        if (!IsProtected(value))
        {
            return false;
        }

        var body = value[Prefix.Length..];
        var sep = body.IndexOf(':');
        if (sep <= 0)
        {
            return false;
        }

        Guid subjectId;
        byte[] blob;
        try
        {
            // new Guid(byte[]) throws ArgumentException (not FormatException) on a non-16-byte array, so a
            // corrupted/format-drifted envelope must degrade to "not revealable", never crash the audit read.
            subjectId = new Guid(Convert.FromBase64String(body[..sep]));
            blob = Convert.FromBase64String(body[(sep + 1)..]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }

        var dek = LoadLiveDek(subjectId); // DB read; null when shredded/missing
        if (dek is null)
        {
            return false;
        }

        try
        {
            plaintext = Encoding.UTF8.GetString(CryptoShredder.Decrypt(blob, dek));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private byte[]? GetOrCreateDek(Guid subjectId)
    {
        using var db = contextFactory();

        // Always read the LIVE key — never trust an in-process cache for encryption, so a shredded subject (the
        // Worker may have erased them) is honoured immediately and the DEK is not retained in memory.
        var existing = db.SubjectKeys.AsNoTracking().FirstOrDefault(k => k.UserId == subjectId);
        if (existing is not null)
        {
            // A shredded/erased key cannot (and must not) be used or re-created.
            return existing is { WrappedDek: not null, DeletedAt: null } ? existing.WrappedDek : null;
        }

        var dek = CryptoShredder.GenerateDek();
        db.SubjectKeys.Add(new SubjectKey { UserId = subjectId, WrappedDek = dek, CreatedAt = clock.UtcNow });
        try
        {
            db.SaveChanges();
            return dek;
        }
        catch (DbUpdateException)
        {
            // A concurrent first-use inserted the row first (UNIQUE UserId). Reload the winner; if NO row exists
            // the failure was not a unique race (e.g. transient DB error) — rethrow rather than silently redacting.
            db.ChangeTracker.Clear();
            var raced = db.SubjectKeys.AsNoTracking().FirstOrDefault(k => k.UserId == subjectId);
            if (raced is null)
            {
                throw;
            }

            return raced is { WrappedDek: not null, DeletedAt: null } ? raced.WrappedDek : null;
        }
    }

    private byte[]? LoadLiveDek(Guid subjectId)
    {
        using var db = contextFactory();
        var key = db.SubjectKeys.AsNoTracking().FirstOrDefault(k => k.UserId == subjectId);
        return key is { WrappedDek: not null, DeletedAt: null } ? key.WrappedDek : null;
    }
}
