namespace ModularPlatform.Abstractions;

/// <summary>
/// Marks a <b>string</b> entity property as personal data (PII). The platform's audit interceptor encrypts such
/// values under the data subject's data-encryption key (DEK) before writing them to the audit trail, so GDPR
/// erasure (shredding the DEK) renders them permanently unrecoverable. The declaring entity MUST also implement
/// <see cref="IDataSubject"/> so the interceptor knows whose key to use — an ArchUnitNET rule enforces that pairing.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PersonalDataAttribute : Attribute;

/// <summary>
/// Marks a <b>string</b> <see cref="PersonalDataAttribute"/> property for encryption AT REST in its LIVE column
/// (not only in the audit trail): the platform's encryption interceptor seals the value under the subject's DEK
/// before every save, and a model-level converter transparently decrypts on read (write context AND the
/// interceptor-free read factory). GDPR erasure (shredding the DEK) renders the stored ciphertext permanently
/// unreadable. The property MUST also be <c>[PersonalData]</c> and its entity <see cref="IDataSubject"/>
/// (ArchUnitNET-enforced). CAVEAT: <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> bypass the interceptor — constants
/// written that way are stored as-is (used deliberately for erasure tombstones).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class EncryptedAttribute : Attribute;

/// <summary>
/// Deterministic keyed-hash (blind index) for looking rows up by an encrypted value — e.g. finding a user by
/// e-mail when <c>users.Email</c> is ciphertext. The key is PLATFORM-WIDE (lookups like login are pre-auth and
/// cross-subject, so a per-subject key is impossible) and secret: without it the index reveals nothing beyond
/// equality. Callers hash the NORMALIZED form (<c>value.Trim().ToUpperInvariant()</c> for e-mails).
/// Implemented by the Gdpr module (HMAC-SHA256 under <c>Gdpr:Encryption:BlindIndexKey</c>).
/// </summary>
public interface IBlindIndexHasher
{
    string Hash(string normalizedValue);
}

/// <summary>
/// An entity carrying <see cref="PersonalDataAttribute"/> fields declares the data subject those fields belong to.
/// <see cref="SubjectId"/> is the user whose DEK protects this row's PII — e.g. the user's own Id, or an owned
/// entity's UserId. Implement EXPLICITLY (<c>Guid IDataSubject.SubjectId =&gt; ...</c>) so EF does not map it as a column.
/// </summary>
public interface IDataSubject
{
    Guid SubjectId { get; }
}

/// <summary>
/// Encrypts/decrypts personal-data values under a per-subject DEK for the audit trail. Implemented by the Gdpr
/// module over its crypto-shredder + the <c>SubjectKey</c> envelope; the platform's audit interceptor depends only
/// on this port (never on a module Core). Erasure = shred the subject's DEK, after which <see cref="TryReveal"/>
/// refuses to decrypt. Implementations must be safe to call from inside a SaveChanges interceptor (no reentrancy
/// into the audited context).
/// </summary>
public interface IPersonalDataProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the subject's DEK (created on first use) and returns a
    /// self-describing envelope string safe to persist in the audit JSON. If the subject's key has already been
    /// shredded, returns <see cref="PersonalDataProtection.RedactedMarker"/> — it never returns the plaintext.
    /// </summary>
    string Protect(Guid subjectId, string plaintext);

    /// <summary>
    /// If <paramref name="value"/> is a protected envelope AND the subject's DEK is still live, decrypts it into
    /// <paramref name="plaintext"/> and returns true. Returns false for a non-envelope value, or when the subject's
    /// key has been shredded (erased).
    /// </summary>
    bool TryReveal(string value, out string plaintext);

    /// <summary>
    /// Like <see cref="TryReveal(string, out string)"/>, but additionally requires the envelope to belong to
    /// <paramref name="expectedSubjectId"/>. Use this when reading PII from a row whose subject is known so a whole
    /// envelope copied from another subject's row is treated as unrevealable.
    /// </summary>
    bool TryRevealForSubject(Guid expectedSubjectId, string value, out string plaintext) =>
        TryReveal(value, out plaintext);

    /// <summary>True if <paramref name="value"/> is a protected envelope (regardless of whether it can still be revealed).</summary>
    bool IsProtected(string value);
}

/// <summary>Shared markers for the personal-data protection scheme (used by the interceptor and audit readers).</summary>
public static class PersonalDataProtection
{
    /// <summary>Stored in place of PII when no protector/subject is available — guarantees plaintext is never persisted.</summary>
    public const string RedactedMarker = "[pii-redacted]";

    /// <summary>Surfaced by audit readers for a protected value whose subject key has been shredded (erased).</summary>
    public const string ErasedMarker = "[erased]";
}
