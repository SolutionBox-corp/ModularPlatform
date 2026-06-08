using System.Security.Cryptography;

namespace ModularPlatform.Gdpr.Security;

/// <summary>
/// Crypto-shredding primitive for GDPR erasure. Each data subject gets one symmetric
/// data-encryption key (DEK); all of the subject's PII is stored encrypted under that DEK.
/// <para>
/// Erasure = <b>delete the DEK</b>. Once the DEK is gone, every ciphertext encrypted under it is
/// permanently unrecoverable, which satisfies "right to be forgotten" even for data sitting in
/// append-only stores or backups that cannot be physically rewritten.
/// </para>
/// <para>
/// This class provides only the cryptographic primitive: generate a DEK, encrypt and decrypt with it.
/// Key STORAGE and wrapping (a KEK in a KMS/HSM, envelope rotation, access policy) are deliberately
/// OUT OF SCOPE — the platform's <c>SubjectKey</c> table models the DEK envelope lifecycle, and a real
/// deployment would wrap the DEK with a KMS-managed KEK before persisting it.
/// </para>
/// Uses AES-256-GCM (authenticated encryption). The serialized blob layout is:
/// <c>[12-byte nonce][16-byte tag][ciphertext]</c>.
/// </summary>
internal static class CryptoShredder
{
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12;  // AES-GCM standard nonce
    private const int TagSizeBytes = 16;    // AES-GCM authentication tag

    /// <summary>Generates a fresh random 256-bit data-encryption key (DEK) for one subject.</summary>
    public static byte[] GenerateDek() => RandomNumberGenerator.GetBytes(KeySizeBytes);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the subject's <paramref name="dek"/>.
    /// Returns <c>[nonce][tag][ciphertext]</c>. Deleting the DEK makes this output unrecoverable.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(dek);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(dek, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
        return output;
    }

    /// <summary>
    /// Decrypts a <c>[nonce][tag][ciphertext]</c> blob produced by <see cref="Encrypt"/> using the
    /// subject's <paramref name="dek"/>. Throws <see cref="CryptographicException"/> if the DEK is wrong
    /// or the blob was tampered with (GCM tag mismatch) — and, after erasure, the DEK no longer exists.
    /// </summary>
    public static byte[] Decrypt(byte[] blob, byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(dek);
        if (blob.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Ciphertext blob is too short to contain a nonce and tag.");
        }

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[blob.Length - NonceSizeBytes - TagSizeBytes];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(blob, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(blob, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(dek, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
