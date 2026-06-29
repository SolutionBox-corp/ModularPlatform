namespace ModularPlatform.Identity.Authorization;

internal sealed class EmailVerificationOptions
{
    public const string SectionName = "Identity:EmailVerification";

    public int TokenLifetimeMinutes { get; set; } = 1440;
    public string VerifyUrl { get; set; } = "http://localhost:3000/verify-email";
}
