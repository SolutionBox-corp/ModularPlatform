namespace ModularPlatform.Storage;

/// <summary>
/// Validates a storage key before it touches a provider. Keys are server-generated opaque ids; this is a
/// defence-in-depth guard so a future caller can't smuggle a path-traversal token (<c>..</c>, leading slash,
/// backslash, rooted path) into the local-disk provider and escape the storage root.
/// </summary>
internal static class StorageKey
{
    public static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Storage key must not be empty.", nameof(key));
        }

        if (key.Contains("..", StringComparison.Ordinal)
            || key.StartsWith('/') || key.StartsWith('\\')
            || key.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(key)
            || key.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ArgumentException($"Invalid storage key '{key}'.", nameof(key));
        }
    }

    private static readonly char[] InvalidChars = ['\0', ':', '*', '?', '"', '<', '>', '|'];
}
