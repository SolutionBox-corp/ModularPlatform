using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ModularPlatform.Persistence;

/// <summary>
/// Fails any <c>SaveChanges</c> on a context from <see cref="IReadDbContextFactory{TContext}"/>. A read context is
/// NoTracking (so a save is normally a no-op), but a deliberate <c>.AsTracking()</c> + <c>SaveChanges</c> would
/// persist WITHOUT the audit interceptor, tenant stamping, or the PII encryption interceptor — writing plaintext
/// into an <c>[Encrypted]</c> column. This guard makes that misuse a loud throw instead.
/// </summary>
internal sealed class ReadOnlyGuardInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result) => throw Guard();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw Guard();

    private static InvalidOperationException Guard() => new(
        "A read context (IReadDbContextFactory) must never persist changes — its writes bypass audit, tenant "
        + "stamping and PII encryption. Use the write context (IDbContextOutbox / the scoped DbContext) to mutate.");
}
