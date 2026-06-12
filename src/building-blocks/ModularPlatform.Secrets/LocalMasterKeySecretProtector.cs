using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Secrets;

/// <summary>
/// Default (dev / self-host) <see cref="ISecretProtector"/>: AES-256-GCM under a versioned application master key.
/// No external dependency, so the platform runs on a plain Postgres box. The serialized blob layout matches the
/// platform's crypto-shred primitive: <c>[12-byte nonce][16-byte tag][ciphertext]</c>. AAD = <c>tenantId|purpose</c>
/// binds each ciphertext to its row context (a swapped blob fails the GCM tag). A KMS-backed envelope provider can
/// replace this later with the same <see cref="ProtectedSecret"/> shape (it would additionally populate WrappedDek).
/// </summary>
internal sealed class LocalMasterKeySecretProtector : ISecretProtector
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    private readonly IReadOnlyDictionary<int, byte[]> _keys;
    private readonly int _activeVersion;

    public LocalMasterKeySecretProtector(IOptions<SecretsOptions> options)
    {
        var value = options.Value;
        var keys = new Dictionary<int, byte[]>();

        foreach (var (versionText, base64) in value.MasterKeys)
        {
            if (int.TryParse(versionText, out var version) && !string.IsNullOrWhiteSpace(base64))
            {
                keys[version] = DecodeKey(base64, version);
            }
        }

        // Dev convenience: no key configured → the well-known placeholder (the validator refuses it outside Development).
        if (keys.Count == 0)
        {
            keys[1] = DecodeKey(SecretsOptions.DevPlaceholderMasterKey, 1);
        }

        _keys = keys;
        _activeVersion = value.ActiveKeyVersion;

        if (!_keys.ContainsKey(_activeVersion))
        {
            throw new InvalidOperationException(
                $"Secrets:ActiveKeyVersion={_activeVersion} has no matching key in Secrets:MasterKeys.");
        }
    }

    public Task<ProtectedSecret> ProtectAsync(Guid? tenantId, string purpose, string plaintext, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = _keys[_activeVersion];
        var aad = BuildAad(tenantId, purpose);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var cipher = new byte[plainBytes.Length];
            var tag = new byte[TagSizeBytes];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, plainBytes, cipher, tag, aad);

            var blob = new byte[NonceSizeBytes + TagSizeBytes + cipher.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, blob, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(cipher, 0, blob, NonceSizeBytes + TagSizeBytes, cipher.Length);

            return Task.FromResult(new ProtectedSecret(_activeVersion, blob));
        }
        finally
        {
            // Wipe the plaintext copy ASAP (the immutable input string itself can't be zeroed — a CLR limitation).
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Task<string> RevealAsync(Guid? tenantId, string purpose, ProtectedSecret secret, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(secret);

        if (!_keys.TryGetValue(secret.KeyVersion, out var key))
        {
            throw new InvalidOperationException(
                $"No Secrets master key for version {secret.KeyVersion} (it may have been retired).");
        }

        var blob = secret.Ciphertext;
        if (blob.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Sealed secret is too short to contain a nonce and tag.");
        }

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var cipher = new byte[blob.Length - NonceSizeBytes - TagSizeBytes];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(blob, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(blob, NonceSizeBytes + TagSizeBytes, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, cipher, tag, plain, BuildAad(tenantId, purpose));
        try
        {
            return Task.FromResult(Encoding.UTF8.GetString(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    // Length-prefixed components so the parts are unambiguous: a purpose containing the '|' separator can never alias a
    // different (tenant, purpose) pair (confused-deputy). Changing this format invalidates older ciphertext — fine here
    // (no persisted secrets predate it; the GCM tag would simply fail and surface as a CryptographicException).
    private static byte[] BuildAad(Guid? tenantId, string purpose)
    {
        var tenant = tenantId?.ToString("N") ?? "platform";
        return Encoding.UTF8.GetBytes($"{tenant.Length}:{tenant}|{purpose.Length}:{purpose}");
    }

    private static byte[] DecodeKey(string base64, int version)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Secrets:MasterKeys[{version}] is not valid base64.");
        }

        if (key.Length != KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Secrets:MasterKeys[{version}] must decode to {KeySizeBytes} bytes (AES-256), got {key.Length}.");
        }

        return key;
    }
}
