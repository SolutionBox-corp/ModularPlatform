namespace ModularPlatform.Secrets;

/// <summary>
/// Binds <c>Secrets:*</c>. <see cref="Provider"/> selects the implementation (<c>local</c> today; <c>aws-kms</c> |
/// <c>azure-kv</c> | <c>vault</c> later). The local provider seals secrets with a versioned application master key.
/// <para>
/// <see cref="MasterKeys"/> maps a numeric version (as a string, so it binds from config) to a base64 32-byte key.
/// <see cref="ActiveKeyVersion"/> is the version used to seal NEW secrets; older versions stay so existing rows can
/// still be revealed until a rotation job re-seals them. NEVER commit a real key — supply it via env / secret store.
/// </para>
/// </summary>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    /// <summary>The well-known dev-only master key (base64 of 32 bytes). Refused outside Development by the validator.</summary>
    public const string DevPlaceholderMasterKey = "ZGV2LW9ubHktc2VjcmV0cy1tYXN0ZXIta2V5LTAwMzI=";

    /// <summary><c>local</c> (app master key, dev/self-host). KMS-backed providers drop in later with the same output shape.</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Version used to seal new secrets. Must have a matching entry in <see cref="MasterKeys"/>.</summary>
    public int ActiveKeyVersion { get; set; } = 1;

    /// <summary>Version (string) → base64-encoded 32-byte AES-256 key. Empty in dev → the placeholder key is used.</summary>
    public Dictionary<string, string> MasterKeys { get; set; } = new();
}
