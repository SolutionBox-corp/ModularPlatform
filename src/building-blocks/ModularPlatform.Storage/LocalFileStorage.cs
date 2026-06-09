using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Storage;

/// <summary>
/// Local-disk <see cref="IFileStorage"/> for dev/test. Writes each blob under the configured root by its opaque
/// server-generated key. The key is validated (<see cref="StorageKey"/>) and the resolved path is re-checked to be
/// inside the root, so a malformed key can never escape the storage directory.
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        var configured = options.Value.Local.RootPath;
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "modularplatform-storage")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Stored object not found.", key);
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Maps an opaque key to a path under the root and verifies it cannot escape (defence in depth).</summary>
    private string ResolvePath(string key)
    {
        StorageKey.Validate(key);
        var rootFull = Path.GetFullPath(_root);
        var path = Path.GetFullPath(Path.Combine(rootFull, key));
        if (!path.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(path, rootFull, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Storage key '{key}' escapes the storage root.", nameof(key));
        }

        return path;
    }
}
