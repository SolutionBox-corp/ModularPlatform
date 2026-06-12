using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Secrets;

/// <summary>
/// Fails the host at startup (via <c>ValidateOnStart</c>) if the local secret provider would seal with the dev
/// placeholder — or no real key — outside Development. A known master key is a confidentiality-fails-open hazard for
/// tenant payment credentials. Development is exempt so local runs work without a configured key. Mirrors
/// <c>JwtOptionsValidator</c> / <c>RlsBootstrapper</c>.
/// </summary>
public sealed class SecretsOptionsValidator(IHostEnvironment environment) : IValidateOptions<SecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretsOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        // Only the local master-key provider is validated here; KMS providers fail-fast on their own client config.
        if (!string.Equals(options.Provider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        var version = options.ActiveKeyVersion.ToString();

        if (options.MasterKeys.Count == 0 || !options.MasterKeys.TryGetValue(version, out var activeKey)
            || string.IsNullOrWhiteSpace(activeKey))
        {
            errors.Add($"Secrets:MasterKeys must contain the active key version {version} outside Development.");
        }

        foreach (var (slot, keyValue) in options.MasterKeys)
        {
            // Reject the dev placeholder in ANY key slot, not just the active one — a retained "legacy decrypt"
            // version that is the well-known placeholder is still usable by any caller that supplies that KeyVersion.
            if (string.Equals(keyValue, SecretsOptions.DevPlaceholderMasterKey, StringComparison.Ordinal))
            {
                errors.Add($"Secrets:MasterKeys[{slot}] must not be the dev placeholder outside Development.");
                continue;
            }

            // Validate base64 + AES-256 length HERE (clear startup error) rather than letting the protector throw an
            // opaque InvalidOperationException on first use / at DI build.
            if (string.IsNullOrWhiteSpace(keyValue))
            {
                continue; // handled by the active-key presence check above
            }

            try
            {
                if (Convert.FromBase64String(keyValue).Length != 32)
                {
                    errors.Add($"Secrets:MasterKeys[{slot}] must decode to 32 bytes (AES-256).");
                }
            }
            catch (FormatException)
            {
                errors.Add($"Secrets:MasterKeys[{slot}] is not valid base64.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
