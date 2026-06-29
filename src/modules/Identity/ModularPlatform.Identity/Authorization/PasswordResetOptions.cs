namespace ModularPlatform.Identity.Authorization;

internal sealed class PasswordResetOptions
{
    public const string SectionName = "Identity:PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 30;
    public string ResetUrl { get; set; } = "http://localhost:3000/reset-password";
}
