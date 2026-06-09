namespace ModularPlatform.Abstractions;

/// <summary>Ambient request identity, sourced from the JWT. Backs RLS tenant scoping and audit.</summary>
public interface ITenantContext
{
    /// <summary>Current tenant; <c>null</c> for platform/system context (jobs, migrations).</summary>
    Guid? TenantId { get; }

    /// <summary>Current authenticated user; <c>null</c> for anonymous/system context.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// True ONLY for a trusted system principal (worker/jobs/migration) that may bypass the tenant query filter.
    /// An authenticated HTTP user is NEVER system — a missing tenant claim must NOT grant cross-tenant access.
    /// </summary>
    bool IsSystem { get; }

    /// <summary>Client IP for audit, if available.</summary>
    string? IpAddress { get; }
}

/// <summary>Testable time source. Never call DateTime.UtcNow directly in handlers.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Pushes a server-&gt;client event to a specific user across all API instances.
/// Implemented over Redis pub/sub fan-out; producers (handlers, worker) stay transport-agnostic
/// so a module can switch SSE-&gt;SignalR without touching this call site.
/// </summary>
public interface IRealtimePublisher
{
    Task PublishToUserAsync(Guid userId, string eventType, object payload, CancellationToken ct = default);

    Task PublishToTenantAsync(Guid tenantId, string eventType, object payload, CancellationToken ct = default);
}

/// <summary>
/// GDPR data-portability port. Each module implements one, returning the personal data it holds
/// for a subject. The Gdpr module fans these out into a single export document.
/// </summary>
public interface IExportPersonalData
{
    /// <summary>Module name, used to key the export section.</summary>
    string ModuleName { get; }

    Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// GDPR erasure port. Each module erases its own slice in response to a UserErasureRequested event,
/// primarily by destroying the subject's encryption key (crypto-shredding) and anonymizing residual
/// non-PII rows that must survive (AML/tax).
/// </summary>
public interface IErasePersonalData
{
    string ModuleName { get; }

    Task EraseAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Blob storage port — the bytes of an uploaded file live behind this, the metadata lives in the owning module.
/// Implemented by a local-disk provider (dev) and an S3-compatible provider (AWS S3 / MinIO / Cloudflare R2).
/// <para>
/// SECURITY: <paramref name="key"/> is a SERVER-GENERATED opaque id (never a client-supplied filename) — a client
/// filename is a path-traversal vector. The provider treats the key as opaque and refuses any path-traversal token.
/// </para>
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/>, overwriting if it already exists.</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>Opens the stored object for reading. Throws if the key does not exist.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct);

    /// <summary>Deletes the stored object. Idempotent — deleting a missing key is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
