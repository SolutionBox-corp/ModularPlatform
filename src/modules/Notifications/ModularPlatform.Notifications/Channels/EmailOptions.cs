namespace ModularPlatform.Notifications.Channels;

/// <summary>SMTP configuration bound from the "Email" configuration section.</summary>
internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@modularplatform.local";
}
