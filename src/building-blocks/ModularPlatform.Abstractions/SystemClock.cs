namespace ModularPlatform.Abstractions;

/// <summary>Production <see cref="IClock"/> backed by the system UTC clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
