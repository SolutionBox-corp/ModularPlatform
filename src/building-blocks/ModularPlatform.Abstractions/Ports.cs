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
/// Resolves a tenant from the request host (subdomain) or its Id. Owned by the Tenancy module; consumed by the
/// tenant-resolution middleware and registration. Returns a public projection — the Tenant Core entity stays internal.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>Resolves the tenant whose subdomain matches (case-insensitive). Null = unknown subdomain.</summary>
    Task<TenantInfo?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default);

    /// <summary>Resolves a tenant by Id. Null = unknown tenant.</summary>
    Task<TenantInfo?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Public projection of a tenant. <see cref="Status"/>/<see cref="Placement"/> are stringified (the Core enum stays internal).</summary>
public sealed record TenantInfo(Guid Id, string Subdomain, string Name, string Status, string Placement);

/// <summary>
/// Per-request answer to "does THIS tenant have module X enabled?" — the single source for nav + the
/// <c>ModuleEntitlementGuard</c>. Owned by Tenancy; entitlements are DB data (never a JWT claim — they would go stale).
/// </summary>
public interface IEntitlementResolver
{
    /// <summary>True if the tenant currently has the module enabled (within its validity window).</summary>
    Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleKey, CancellationToken ct = default);

    /// <summary>All of the tenant's module entitlements + tier (drives FE nav). Empty modules = nothing entitled.</summary>
    Task<TenantEntitlementsView> GetForTenantAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Public view of a tenant's entitlements: its plan tier + the per-module enabled flags.</summary>
public sealed record TenantEntitlementsView(Guid TenantId, string? Tier, IReadOnlyList<ModuleEntitlementView> Modules);

/// <summary>One module's entitlement for a tenant.</summary>
public sealed record ModuleEntitlementView(string Key, bool Enabled, string? Tier);

/// <summary>
/// Provisions a tenant row. Owned by Tenancy; consumed by registration (interim auto-provision) and the
/// platform-admin provisioning flow. Returns the new tenant Id. A null subdomain is auto-generated (unique).
/// </summary>
public interface ITenantProvisioning
{
    Task<Guid> CreateAsync(string name, string? subdomain = null, CancellationToken ct = default);

    /// <summary>
    /// Best-effort removal of a just-provisioned tenant (and its default entitlements). The COMPENSATION for a
    /// self-serve registration that provisioned a tenant but then failed to create the user (e.g. a concurrent
    /// email-uniqueness race) — without it the failed registration leaks an orphan, owner-less tenant. Idempotent:
    /// a missing tenant is a no-op.
    /// </summary>
    Task DeleteAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Gate consulted when a user signs up ON a tenant's subdomain (a JOIN). Enforces the tenant's
/// <c>RegistrationMode</c>: <c>Open</c> always allows; <c>Closed</c> always denies; <c>InviteOnly</c> requires a
/// valid single-use invite token, which it CONSUMES on success. Owned by Tenancy; consumed by Identity's
/// registration. A null implementation (Tenancy disabled) means no subdomain ever resolves, so a join never reaches
/// this gate. Identity NEVER reads the registry directly.
/// </summary>
public interface ITenantRegistrationGate
{
    /// <summary>True if the join is permitted (and any required invite has now been consumed); false if denied.</summary>
    Task<bool> TryAcceptJoinAsync(Guid tenantId, string? inviteToken, CancellationToken ct = default);
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
/// Tenant- (or platform-) scoped secret-at-rest port. Seals credentials a tenant configures (payment-gateway API
/// keys / webhook secrets; later device/agent credentials) under a key whose SCOPE is the tenant or the platform —
/// NEVER the per-subject DEK (that is destroyed on GDPR erasure; a tenant's live payment keys must survive).
/// <para>
/// Provider is selected by <c>Secrets:Provider</c> = <c>local</c> (dev/self-host: an app master key, AES-256-GCM)
/// today; a KMS-backed envelope impl (AWS KMS / Azure Key Vault / Vault) drops in later with the SAME output shape.
/// Async because the KMS impl is a network call. AAD binds the ciphertext to its <c>(tenantId, purpose)</c> row, so a
/// swapped blob fails authentication. Persist <see cref="ProtectedSecret"/> as bytes — NEVER the plaintext, and never
/// route a secret through audit / an outbox envelope / logs (carry a <c>(tenantId, purpose)</c> reference instead).
/// </para>
/// </summary>
public interface ISecretProtector
{
    /// <summary>Seals <paramref name="plaintext"/> for <paramref name="tenantId"/> (null = platform-scoped) under the active key. AAD = tenant + purpose.</summary>
    Task<ProtectedSecret> ProtectAsync(Guid? tenantId, string purpose, string plaintext, CancellationToken ct = default);

    /// <summary>Unseals a value produced by <see cref="ProtectAsync"/>. The same <paramref name="tenantId"/>/<paramref name="purpose"/> must be supplied (they form the AAD).</summary>
    Task<string> RevealAsync(Guid? tenantId, string purpose, ProtectedSecret secret, CancellationToken ct = default);
}

/// <summary>
/// A sealed secret. <see cref="Ciphertext"/> is the self-describing <c>[nonce][tag][ciphertext]</c> blob.
/// <see cref="KeyVersion"/> selects the master/KEK version that sealed it (so rotation can re-seal old rows).
/// <see cref="WrappedDek"/> is null for the local-master-key provider and carries the KMS-wrapped DEK for the
/// envelope provider — present from day 1 so the KMS impl is a drop-in with no schema change.
/// </summary>
public sealed record ProtectedSecret(int KeyVersion, byte[] Ciphertext, byte[]? WrappedDek = null);

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
