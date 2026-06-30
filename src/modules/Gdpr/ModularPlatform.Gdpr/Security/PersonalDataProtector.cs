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
internal sealed class PersonalDataProtector(
    Func<GdprDbContext> contextFactory, IClock clock, ISecretProtector secretProtector) : IPersonalDataProtector
{
    // v1: AES-GCM without AAD (legacy audit envelopes stay readable until natural erasure).
    // v2: AES-GCM with AAD = subjectId bytes — an envelope re-attached to another subject fails authentication.
    private const string PrefixV1 = "penc:v1:";
    private const string PrefixV2 = "penc:v2:";

    // The DEK is WRAPPED under the application master key (via ISecretProtector) before it is persisted, so
    // subject_keys never holds a usable key — a DB dump alone can't decrypt PII. AAD binds the wrap to the subject.
    private const string DekPurpose = "gdpr.subject_dek";

    public bool IsProtected(string value) =>
        value is not null
        && (value.StartsWith(PrefixV2, StringComparison.Ordinal)
            || value.StartsWith(PrefixV1, StringComparison.Ordinal));

    public string Protect(Guid subjectId, string plaintext)
    {
        var dek = GetOrCreateDek(subjectId);
        if (dek is null)
        {
            // The subject's key is already shredded — never write plaintext, redact instead.
            return PersonalDataProtection.RedactedMarker;
        }

        var blob = CryptoShredder.Encrypt(Encoding.UTF8.GetBytes(plaintext), dek, aad: subjectId.ToByteArray());
        return $"{PrefixV2}{Convert.ToBase64String(subjectId.ToByteArray())}:{Convert.ToBase64String(blob)}";
    }

    public bool TryReveal(string value, out string plaintext)
    {
        plaintext = string.Empty;
        if (!TryParseEnvelope(value, out var parsed))
        {
            return false;
        }

        return TryRevealParsed(parsed, out plaintext);
    }

    public bool TryRevealForSubject(Guid expectedSubjectId, string value, out string plaintext)
    {
        plaintext = string.Empty;
        if (!TryParseEnvelope(value, out var parsed))
        {
            return false;
        }

        if (parsed.SubjectId != expectedSubjectId)
        {
            return false;
        }

        return TryRevealParsed(parsed, out plaintext);
    }

    private static bool TryParseEnvelope(string value, out ParsedEnvelope parsed)
    {
        parsed = default;
        if (value is null)
        {
            return false;
        }

        var v2 = value.StartsWith(PrefixV2, StringComparison.Ordinal);
        if (!v2 && !value.StartsWith(PrefixV1, StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[(v2 ? PrefixV2 : PrefixV1).Length..];
        var sep = body.IndexOf(':');
        if (sep <= 0)
        {
            return false;
        }

        try
        {
            // new Guid(byte[]) throws ArgumentException (not FormatException) on a non-16-byte array, so a
            // corrupted/format-drifted envelope must degrade to "not revealable", never crash the audit read.
            parsed = new ParsedEnvelope(
                new Guid(Convert.FromBase64String(body[..sep])),
                Convert.FromBase64String(body[(sep + 1)..]),
                v2);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private bool TryRevealParsed(ParsedEnvelope parsed, out string plaintext)
    {
        plaintext = string.Empty;
        var dek = LoadLiveDek(parsed.SubjectId); // DB read; null when shredded/missing
        if (dek is null)
        {
            return false;
        }

        try
        {
            plaintext = Encoding.UTF8.GetString(
                CryptoShredder.Decrypt(parsed.Blob, dek, aad: parsed.V2 ? parsed.SubjectId.ToByteArray() : null));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private readonly record struct ParsedEnvelope(Guid SubjectId, byte[] Blob, bool V2);

    private byte[]? GetOrCreateDek(Guid subjectId)
    {
        using var db = contextFactory();

        // Always read the LIVE key — never trust an in-process cache for encryption, so a shredded subject (the
        // Worker may have erased them) is honoured immediately and the DEK is not retained in memory.
        var existing = db.SubjectKeys.AsNoTracking().FirstOrDefault(k => k.UserId == subjectId);
        if (existing is not null)
        {
            // A shredded/erased key cannot (and must not) be used or re-created.
            return Unwrap(subjectId, existing);
        }

        var dek = CryptoShredder.GenerateDek();
        var sealedDek = secretProtector.ProtectAsync(subjectId, DekPurpose, Convert.ToBase64String(dek))
            .GetAwaiter().GetResult();
        db.SubjectKeys.Add(new SubjectKey
        {
            UserId = subjectId,
            WrappedDek = sealedDek.Ciphertext,
            DekKeyVersion = sealedDek.KeyVersion,
            CreatedAt = clock.UtcNow,
        });
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

            return Unwrap(subjectId, raced);
        }
    }

    private byte[]? LoadLiveDek(Guid subjectId)
    {
        using var db = contextFactory();
        var key = db.SubjectKeys.AsNoTracking().FirstOrDefault(k => k.UserId == subjectId);
        return Unwrap(subjectId, key);
    }

    /// <summary>Unwraps the persisted (master-key-encrypted) DEK; null for a missing/shredded key.</summary>
    private byte[]? Unwrap(Guid subjectId, SubjectKey? key)
    {
        if (key is not { WrappedDek: { } wrapped, DeletedAt: null })
        {
            return null;
        }

        var dekBase64 = secretProtector
            .RevealAsync(subjectId, DekPurpose, new ProtectedSecret(key.DekKeyVersion, wrapped))
            .GetAwaiter().GetResult();
        return Convert.FromBase64String(dekBase64);
    }
}
