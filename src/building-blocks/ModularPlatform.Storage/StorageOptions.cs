namespace ModularPlatform.Storage;

/// <summary>
/// Binds <c>Storage:*</c>. <see cref="Provider"/> selects the implementation (<c>local</c> | <c>s3</c>, default
/// <c>local</c>). The S3 settings (bucket + optional endpoint) come ONLY from config — never from a request — so a
/// caller can never redirect a write to another bucket/host.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary><c>local</c> (disk, dev) or <c>s3</c> (AWS S3 / MinIO / Cloudflare R2).</summary>
    public string Provider { get; set; } = "local";

    public LocalStorageOptions Local { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();
}

/// <summary>Local-disk provider settings. Objects are written under <see cref="RootPath"/> by their opaque key.</summary>
public sealed class LocalStorageOptions
{
    /// <summary>Root directory for stored blobs. Defaults to a temp dir if unset (dev only).</summary>
    public string? RootPath { get; set; }
}

/// <summary>
/// S3-compatible provider settings. <see cref="ServiceUrl"/> + <see cref="ForcePathStyle"/> let the SAME provider
/// target AWS S3 (leave ServiceUrl empty), MinIO or Cloudflare R2 (set ServiceUrl + ForcePathStyle=true).
/// </summary>
public sealed class S3StorageOptions
{
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Custom endpoint for MinIO/R2. Empty → real AWS S3 resolved from <see cref="Region"/>.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>AWS region (when talking to real S3). Ignored when <see cref="ServiceUrl"/> is set.</summary>
    public string? Region { get; set; }

    /// <summary>Required for MinIO and most R2/S3-compatible endpoints.</summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Static credentials (optional — falls back to the AWS default credential chain when empty).</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }
}
