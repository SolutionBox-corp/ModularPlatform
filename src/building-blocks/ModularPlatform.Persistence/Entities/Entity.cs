namespace ModularPlatform.Persistence.Entities;

/// <summary>
/// Base for every persisted aggregate. Flat — relationships are by Id only, never navigation
/// includes (enforced across module boundaries). Optimistic concurrency is the Postgres <c>xmin</c>
/// system column, applied by convention, so there is no RowVersion property to maintain here.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Adds create/update audit stamps, set automatically by the audit interceptor.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Marks an entity as tenant-scoped: gets a shadow <c>TenantId</c> + global query filter + RLS.</summary>
public interface ITenantScoped;

/// <summary>
/// Marks an entity owned by exactly one user. Defence-in-depth: the RLS bootstrapper gives its table a
/// Postgres row-level-security policy keyed on the owner column, so a forgotten <c>WHERE UserId == …</c>
/// in app code still cannot leak another user's rows — the database rejects them. The entity MUST expose
/// a non-null <c>Guid UserId</c> column (the default owner column the policy filters on).
/// </summary>
public interface IUserOwned;

/// <summary>Marks an entity as soft-deletable (kept out of the GDPR-erasure default path).</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
