namespace ModularPlatform.Web;

/// <summary>JWT settings, bound from configuration section <c>Jwt</c>.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC signing key (min 32 bytes). In production inject from env/KeyVault, never appsettings.</summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Access-token lifetime in minutes (short — 5–10).</summary>
    public int AccessTokenMinutes { get; init; } = 10;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; init; } = 30;
}
