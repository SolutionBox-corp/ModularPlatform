namespace ModularPlatform.Cqrs;

/// <summary>
/// In-process CQRS dispatcher. Sends a command or query through its behavior pipeline
/// to the single registered handler. This is the seam: swap the implementation without
/// touching a single handler.
/// </summary>
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);

    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
