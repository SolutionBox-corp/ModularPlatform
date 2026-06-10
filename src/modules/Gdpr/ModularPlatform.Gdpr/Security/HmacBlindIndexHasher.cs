using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Gdpr.Security;

/// <summary>
/// Options for the platform-wide blind-index key, bound from <c>Gdpr:Encryption</c>. The key is a SECRET
/// (>= 32 chars): with it, equality lookups on encrypted columns work; without it the index reveals nothing.
/// Outside Development the placeholder is refused at startup (same fail-fast posture as the JWT signing key).
/// </summary>
internal sealed class GdprEncryptionOptions
{
    public const string SectionName = "Gdpr:Encryption";
    public const string DevKeyPlaceholder = "dev-only-blind-index-key-not-secret";

    public string BlindIndexKey { get; set; } = string.Empty;
}

internal sealed class GdprEncryptionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<GdprEncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, GdprEncryptionOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.BlindIndexKey)
            || options.BlindIndexKey == GdprEncryptionOptions.DevKeyPlaceholder)
        {
            return ValidateOptionsResult.Fail(
                "Gdpr:Encryption:BlindIndexKey must be set to a real secret outside Development.");
        }

        return options.BlindIndexKey.Length < 32
            ? ValidateOptionsResult.Fail("Gdpr:Encryption:BlindIndexKey must be at least 32 characters.")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Gdpr's <see cref="IBlindIndexHasher"/>: HMAC-SHA256 over the caller-normalized value under the platform-wide
/// blind-index key, Base64-encoded (44 chars). Deterministic by design — that is what makes the index usable
/// for equality lookups (login by e-mail) and what makes the KEY the secret, not the algorithm.
/// </summary>
internal sealed class HmacBlindIndexHasher(IOptions<GdprEncryptionOptions> options) : IBlindIndexHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        string.IsNullOrWhiteSpace(options.Value.BlindIndexKey)
            ? GdprEncryptionOptions.DevKeyPlaceholder
            : options.Value.BlindIndexKey);

    public string Hash(string normalizedValue)
    {
        ArgumentNullException.ThrowIfNull(normalizedValue);
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedValue)));
    }
}
