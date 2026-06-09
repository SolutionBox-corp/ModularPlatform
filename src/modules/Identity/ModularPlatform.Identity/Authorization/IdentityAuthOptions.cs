namespace ModularPlatform.Identity.Authorization;

/// <summary>
/// Authorization seeding config. <see cref="AdminEmails"/> lists the users (by email) who are granted the system
/// <c>admin</c> role on startup if/when that user exists — this is how the FIRST admin is bootstrapped without
/// seeding a password. Set it from configuration/secret in each environment.
/// </summary>
public sealed class IdentityAuthOptions
{
    public const string SectionName = "Identity:Auth";

    public string[] AdminEmails { get; set; } = [];
}
