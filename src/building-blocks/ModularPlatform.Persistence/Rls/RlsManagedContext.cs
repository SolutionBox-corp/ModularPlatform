namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// A module DbContext type whose model the RLS bootstrapper inspects for <c>IUserOwned</c> entities.
/// Registered once per module by <c>AddModuleDbContext</c>; the bootstrapper resolves all of them to derive
/// which tables need a policy — so module authors get RLS just by marking an entity, nothing host-side.
/// </summary>
public sealed record RlsManagedContext(Type ContextType);
